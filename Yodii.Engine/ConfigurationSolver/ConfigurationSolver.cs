﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CK.Core;
using System.Diagnostics;
using Yodii.Model;
using System.Collections.ObjectModel;

namespace Yodii.Engine
{
    class ConfigurationSolver
    {
        Dictionary<string,ServiceData> _services;
        List<ServiceRootData> _serviceRoots;
        Dictionary<Guid,PluginData> _plugins;

        internal ConfigurationSolver()
        {
            _services = new Dictionary<string, ServiceData>();
            _serviceRoots = new List<ServiceRootData>();
            _plugins = new Dictionary<Guid, PluginData>();
        }

        internal IYodiiEngineResult StaticResolution( FinalConfiguration finalConfig, IDiscoveredInfo info )
        {
            // Registering all Services.
            _services.Clear();
            _serviceRoots.Clear();

            foreach( IServiceInfo sI in info.ServiceInfos )
            {
                // This creates services and applies solved configuration to them: directly disabled services
                // and specializations disabled by their generalizations' configuration are handled.
                RegisterService( finalConfig, sI );
            }
            // Service trees have been built and we have the roots.
            // We can now handle Running services: there must be at most one such service by service 
            // root otherwise it is a configuration error.
            foreach( var root in _serviceRoots )
            {
                if( !root.Disabled ) root.InitializeRunningService();
            }
            // We can now instantiate plugin data. 
            _plugins.Clear();
            foreach( IPluginInfo p in info.PluginInfos )
            {
                RegisterPlugin( finalConfig, p );
            }
            // Initialize services disabled state based on their available plugins:
            // roots without any available plugins are de facto disabled.
            foreach( var root in _serviceRoots )
            {
                if( !root.Disabled ) root.OnAllPluginsAdded();
            }
            // Now, we apply ServiceReference Running constraints from every plugins to their referenced services.
            foreach( PluginData p in _plugins.Values )
            {
                // When a plugin is disabled because of a disabled required service reference and it implements a service, the service
                // becomes disabled (if it has no more available implementations) and that triggers disabling of plugins that require
                // the service. This works because disable flag on each participant is carefully set before propagating the
                // information to others to avoid loops and because such plugins reference themselves at the required service (AddRunnableReferencer).
                if( !p.Disabled && p.ConfigSolvedStatus >= SolvedConfigurationStatus.Runnable )
                {
                    p.CheckReferencesWhenMustExist();
                }
            }
            // Time to conclude about configuration and to initialize dynamic resolution.
            // Any Plugin that has a ConfigOriginalStatus greater or equal to Runnable and is Disabled leads to an impossible configuration.
            List<PluginData> blockingPlugins = null;
            List<ServiceData> blockingServices = null;

            foreach ( PluginData p in _plugins.Values )
            {
                if ( p.Disabled )
                {
                    if ( p.ConfigOriginalStatus >= ConfigurationStatus.Runnable )
                    {
                        if ( blockingPlugins == null ) blockingPlugins = new List<PluginData>();
                        blockingPlugins.Add( p );
                    }
                }
            }
            // Any Service that has a ConfigSolvedStatus greater or equal to Runnable and is Disabled leads to an impossible configuration.
            foreach ( ServiceData s in _services.Values )
            {
                if ( s.Disabled )
                {
                    if ( s.ConfigOriginalStatus >= ConfigurationStatus.Runnable )
                    {
                        if ( blockingServices == null ) blockingServices = new List<ServiceData>();
                        blockingServices.Add( s );
                    }
                }
            }
            if ( blockingPlugins != null || blockingServices != null )
            {
                return new YodiiEngineResult(_services, _plugins, blockingPlugins, blockingServices );
            }
            return SuccessYodiiEngineResult.Default;
        }

        /// <summary>
        /// Solves undetermined status based on commands.
        /// </summary>
        /// <param name="commands"></param>
        /// <returns>This method returns a Tuple <IEnumerable<IPluginInfo>,IEnumerable<IPluginInfo>,IEnumerable<IPluginInfo>> to the host.
        /// Plugins are either disabled, stopped (but can be started) or running (locked or not).</returns>
        internal DynamicSolverResult DynamicResolution( IEnumerable<YodiiCommand> pastCommands, YodiiCommand newOne = null )
        {
            foreach( var s in _services.Values ) s.ResetDynamicState();
            foreach( var p in _plugins.Values ) p.ResetDynamicState();
            
            #if DEBUG
            foreach( var s in _services.Values ) s.OnAllPluginsDynamicStateInitialized();
            #endif

            List<YodiiCommand> commands = new List<YodiiCommand>();
            if( newOne != null )
            {
                bool alwaysTrue = ApplyAndTellMeIfCommandMustBeKept( newOne );
                Debug.Assert( alwaysTrue, "The newly added command is necessarily okay." );
                commands.Add( newOne );
            }

            foreach( var previous in pastCommands )
            {
                if( newOne == null || newOne.CallerKey != previous.CallerKey || newOne.ServiceFullName != previous.ServiceFullName || newOne.PluginId != previous.PluginId )
                {
                    if( ApplyAndTellMeIfCommandMustBeKept( previous ) )
                    {
                        commands.Add( previous );
                    }
                }
            }
            foreach( var s in _services.Values.OrderBy( s => s.ServiceInfo.ServiceFullName ) )
            {
                if( s.DynamicStatus == null ) s.StopBy( ServiceRunningStatusReason.StoppedByFinalDecision );
                else if( s.DynamicStatus.Value >= RunningStatus.Running ) s.EnsureRunningPlugin();
            }

            List<IPluginInfo> disabled = new List<IPluginInfo>();
            List<IPluginInfo> stopped = new List<IPluginInfo>();
            List<IPluginInfo> running = new List<IPluginInfo>();

            foreach( var p in _plugins.Values )
            {
                if( p.DynamicStatus != null )
                {
                    if( p.DynamicStatus.Value == RunningStatus.Disabled ) disabled.Add( p.PluginInfo );
                    else if( p.DynamicStatus.Value == RunningStatus.Stopped ) stopped.Add( p.PluginInfo );
                    else running.Add( p.PluginInfo );
                }
                else
                {
                    Debug.Assert( p.Service == null );
                    p.StopByFinalDecision();
                    stopped.Add( p.PluginInfo );
                }
            }
            return new DynamicSolverResult( disabled.AsReadOnlyList(), stopped.AsReadOnlyList(), running.AsReadOnlyList(), commands.AsReadOnlyList() );
        }
        
        bool ApplyAndTellMeIfCommandMustBeKept( YodiiCommand cmd )
        {
            if ( cmd.ServiceFullName != null )
            {
                ServiceData s = _services[cmd.ServiceFullName];
                if ( s != null )
                {
                    if ( cmd.Start ) return s.StartByCommand();
                    return s.StopByCommand();
                }
                return true;
            }
            // Starts or stops the plugin.
            PluginData p = _plugins[cmd.PluginId];
            if ( p != null )
            {
                if ( cmd.Start ) return p.StartByCommand( cmd.Impact );
                else return p.StopByCommand();
            }
            return true;
        }

        ServiceData RegisterService( FinalConfiguration finalConfig, IServiceInfo s )
        {
            ServiceData data;
            if( _services.TryGetValue( s.ServiceFullName, out data ) ) return data;

            //Set default status
            ConfigurationStatus serviceStatus = finalConfig.GetStatus( s.ServiceFullName );
            // Handle generalization.
            ServiceData dataGen = null;
            if( s.Generalization != null )
            {
                dataGen = RegisterService( finalConfig, s.Generalization );
            }
            Debug.Assert( (s.Generalization == null) == (dataGen == null) );
            if( dataGen == null )
            {
                var dataRoot = new ServiceRootData( _services, s, serviceStatus, externalService => true );
                _serviceRoots.Add( dataRoot );
                data = dataRoot;
            }
            else
            {
                data = new ServiceData( _services, s, dataGen, serviceStatus, externalService => true );
            }
            _services.Add( s.ServiceFullName, data );
            return data;
        }

        PluginData RegisterPlugin( FinalConfiguration finalConfig, IPluginInfo p )
        {
            PluginData data;
            if( _plugins.TryGetValue( p.PluginId, out data ) ) return data;

            //Set default status
            ConfigurationStatus pluginStatus = finalConfig.GetStatus( p.PluginId.ToString() );
            ServiceData service = p.Service != null ? _services[p.Service.ServiceFullName] : null;
            data = new PluginData( _services, p, service, pluginStatus );
            _plugins.Add( p.PluginId, data );
            return data;
        }

        internal IYodiiEngineResult CreateDynamicFailureResult( IEnumerable<Tuple<IPluginInfo, Exception>> errors )
        {
            return new YodiiEngineResult( _services, _plugins, errors );
        }

        internal void UpdateNewResultInLiveInfo( LiveInfo liveInfo )
        {
            Debug.Assert( liveInfo != null );
            for( int i = 0; i < liveInfo.Services.Count; i++ )
            {
                ServiceData serviceData;
                if( _services.TryGetValue( liveInfo.Services[i].ServiceInfo.ServiceFullName, out serviceData ) )
                {
                    liveInfo.UpdateInfo( serviceData );
                }
                else
                {
                    liveInfo.Remove( liveInfo.Services[i--] );
                }
            }
            foreach( var s in _services.Values )
            {
                if( !liveInfo.Contains( s.ServiceInfo.ServiceFullName ) )
                {
                    liveInfo.AddInfo( s );
                }
            }
            for( int i = 0; i < liveInfo.Plugins.Count; i++ )
            {
                PluginData pluginData;
                if( _plugins.TryGetValue( liveInfo.Plugins[i].PluginInfo.PluginId, out pluginData ) )
                {
                    liveInfo.UpdateInfo( pluginData );
                }
                else
                {
                    liveInfo.Remove( liveInfo.Plugins[i--] );
                }
            }
            foreach( var p in _plugins.Values )
            {
                if( !liveInfo.Contains( p.PluginInfo.PluginId ) )
                {
                    liveInfo.AddInfo( p );
                }
            }

            liveInfo.CreateGraphOfDependencies();
        }

    }
}