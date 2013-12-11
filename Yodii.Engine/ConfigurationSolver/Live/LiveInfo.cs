﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Yodii.Model;
using CK.Core;
using System.ComponentModel;
using System.Diagnostics;

namespace Yodii.Engine
{
    class LiveInfo : ILiveInfo
    {
        readonly YodiiEngine _engine;
        readonly CKObservableSortedArrayKeyList<LivePluginInfo,Guid> _plugins;
        readonly CKObservableSortedArrayKeyList<LiveServiceInfo,string> _services;

        internal LiveInfo(YodiiEngine engine)
        {
            Debug.Assert( engine != null );
            _engine = engine;
            _plugins = new CKObservableSortedArrayKeyList<LivePluginInfo, Guid>( l => l.PluginInfo.PluginId );
            _services = new CKObservableSortedArrayKeyList<LiveServiceInfo, string>( l => l.ServiceInfo.ServiceFullName );
        }

        public ICKObservableReadOnlyList<ILivePluginInfo> Plugins
        {
            get { return _plugins; }
        }

        public ICKObservableReadOnlyList<ILiveServiceInfo> Services
        {
            get { return _services; }
        }

        public ILiveServiceInfo FindService( string fullName )
        {
            if( fullName == null ) throw new ArgumentNullException( "fullName" );
            return _services.GetByKey( fullName );
        }

        public ILivePluginInfo FindPlugin( Guid pluginId )
        {
            if( pluginId == null ) throw new ArgumentNullException( "pluginId" );
            return _plugins.GetByKey( pluginId );
        }

        public bool Contains( string serviceFullName )
        {
            return _services.Contains( serviceFullName );
        }

        public bool Contains( Guid pluginId )
        {
            return _plugins.Contains( pluginId );
        }

        internal void UpdateInfo( ServiceData serviceData )
        {
            Debug.Assert( serviceData != null );
            LiveServiceInfo serviceInfo = _services.GetByKey( serviceData.ServiceInfo.ServiceFullName );

            Debug.Assert( serviceInfo != null );
            serviceInfo.UpdateInfo( serviceData );
        }

        internal void UpdateInfo( PluginData pluginData )
        {
            Debug.Assert( pluginData != null );
            LivePluginInfo pluginInfo = _plugins.GetByKey( pluginData.PluginInfo.PluginId );
            Debug.Assert( pluginInfo != null );
            pluginInfo.UpdateInfo( pluginData );    
        }

        internal void Remove( ILiveServiceInfo liveServiceInfo )
        {
            Debug.Assert( liveServiceInfo != null );
            Debug.Assert( _services.Contains( liveServiceInfo ) );

            _services.Remove( liveServiceInfo.ServiceInfo.ServiceFullName );
        }

        internal void Remove( ILivePluginInfo livePluginInfo )
        {
            Debug.Assert( livePluginInfo != null );
            Debug.Assert( _services.Contains( livePluginInfo ) );

            _plugins.Remove( livePluginInfo.PluginInfo.PluginId );
        }

        internal void AddInfo( ServiceData serviceData )
        {
            Debug.Assert( serviceData != null );
            Debug.Assert( !_services.Contains( serviceData.ServiceInfo.ServiceFullName ) );
            _services.Add( new LiveServiceInfo( serviceData, _engine ) );
        }

        internal void AddInfo( PluginData pluginData )
        {
            Debug.Assert( pluginData != null );
            Debug.Assert( !_plugins.Contains( pluginData.PluginInfo.PluginId ) );
            _plugins.Add( new LivePluginInfo( pluginData, _engine ) );
        }

        internal void UpdateRuntimeErrors( IEnumerable<Tuple<IPluginInfo, Exception>> errors )
        {
            foreach( var e in errors )
            {
                LivePluginInfo pluginInfo = _plugins.GetByKey( e.Item1.PluginId );
                if( pluginInfo != null )
                {
                    pluginInfo.CurrentError = e.Item2;
                }
                else
                {
                    Debug.Fail( "The plugin cannot be not found in UpdateRuntimeErrors function" );
                }
            }
        }

        /// <summary>
        /// Called by YodiiEngine.Stop().
        /// </summary>
        internal void Clear()
        {
            _plugins.Clear();
            _services.Clear();
        }

        internal void CreateGraphOfDependencies()
        {
            foreach( var p in _plugins )
            {
                if( p.PluginInfo.Service != null ) p.Service = _services.GetByKey( p.PluginInfo.Service.ServiceFullName );
                else p.Service = null;
            }
            foreach( var s in _services )
            {
                if( s.ServiceInfo.Generalization != null ) s.Generalization = _services.GetByKey( s.ServiceInfo.Generalization.ServiceFullName );
                else s.Generalization = null;
            }
        }

        public IYodiiEngineResult RevokeCaller( string callerKey )
        {
            return _engine.RevokeYodiiCommandCaller( callerKey );
        }
    }
}
