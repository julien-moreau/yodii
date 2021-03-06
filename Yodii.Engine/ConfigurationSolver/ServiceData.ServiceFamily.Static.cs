﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using Yodii.Model;

namespace Yodii.Engine
{
    partial class ServiceData 
    {

        public partial class ServiceFamily
        {
            ServiceData _runningService;
            PluginData _runningPlugin;
            readonly HashSet<ServiceData> _availableServices;

            public ServiceFamily( IConfigurationSolver solver, ServiceData root )
            {
                Solver = solver;
                Root = root;
                _availableServices = new HashSet<ServiceData>();
            }

            public readonly IConfigurationSolver Solver;

            public readonly ServiceData Root;

            public ServiceData RunningService { get { return _runningService; } }

            public PluginData RunningPlugin { get { return _runningPlugin; } }

            public ISet<ServiceData> AvailableServices
            {
                get { return _availableServices; }
            }

            public bool SetRunningPlugin( PluginData p )
            {
                Debug.Assert( p.Service != null && p.Service.Family == this );
                Debug.Assert( p.ConfigSolvedStatus == SolvedConfigurationStatus.Running, "ConfigSolvedStatus must have been set to Running prior calling this." );
                if( _runningPlugin == p ) return !p.Disabled;
                if( p.Disabled ) return false;

                if( _runningPlugin != null )
                {
                    p.SetDisabled( PluginDisabledReason.AnotherRunningPluginExistsInFamily );
                    if( !_runningPlugin.Disabled ) _runningPlugin.SetDisabled( PluginDisabledReason.AnotherRunningPluginExistsInFamily );
                    return false;
                }

                // Sets running plugin before calling SetRunningService.
                _runningPlugin = p;
                if( !SetRunningService( p.Service, ServiceSolvedConfigStatusReason.FromRunningPlugin ) )
                {
                    p.SetDisabled( PluginDisabledReason.ServiceCanNotBeRunning );
                    return false;
                }

                // We must disable what SetRunningService did not disabled: the sibling plugins and any specializations.
                PluginData sibling = p.Service.FirstPlugin;
                while( sibling != null )
                {
                    if( sibling != p && !sibling.Disabled ) sibling.SetDisabled( PluginDisabledReason.SiblingRunningPlugin );
                    sibling = sibling.NextPluginForService;
                }
                ServiceData s = p.Service.FirstSpecialization;
                while( s != null )
                {
                    if( !s.Disabled ) s.SetDisabled( ServiceDisabledReason.PluginRunningAbove );
                    s = s.NextSpecialization;
                }
                return !p.Disabled;
            }

            public bool SetRunningService( ServiceData s, ServiceSolvedConfigStatusReason reason )
            {
                Debug.Assert( s != null && s.Family == this );
                if( s._configSolvedStatus != SolvedConfigurationStatus.Running )
                {
                    s._configSolvedStatus = SolvedConfigurationStatus.Running;
                    s._configSolvedStatusReason = reason;
                }
                if( _runningService == s ) return !s.Disabled;
                if( s.Disabled ) return false;

                if( _runningService != null )
                {
                    // If there is already a current running service, we ONLY accept a more specialized 
                    // running service than the current one: if this service is not a specialization, we reject the change.
                    if( !_runningService.IsStrictGeneralizationOf( s ) )
                    {
                        ServiceDisabledReason r = Solver.Step == ConfigurationSolverStep.RegisterServices ? ServiceDisabledReason.AnotherServiceIsRunningByConfig : ServiceDisabledReason.AnotherServiceRunningInFamily;
                        s.SetDisabled( r );
                        if( !Root.Disabled ) Root.SetDisabled( ServiceDisabledReason.AtLeastTwoSpecializationsMustRun );
                        return false;
                    }
                }

                if( _runningPlugin != null )
                {
                    if( !s.IsGeneralizationOf( _runningPlugin.Service ) )
                    {
                        s.SetDisabled( ServiceDisabledReason.PluginRunningElsewhere );
                        return false;
                    }
                }

                // We first set a running configSolvedStatus from this service up to the root.
                // We do not propagate anything here since the most specialized service necessarily "contains"
                // the propagation of its ancestors.
                var g = s.Generalization;
                while( g != null )
                {
                    if( g._configSolvedStatus != SolvedConfigurationStatus.Running )
                    {
                        g._configSolvedStatus = SolvedConfigurationStatus.Running;
                        g._configSolvedStatusReason = ServiceSolvedConfigStatusReason.FromRunningSpecialization;
                    }
                    g = g.Generalization;
                }

                var prevRunningService = _runningService;
                _runningService = s;
                // We must now disable all sibling services (and plugins) from this up to the currently running one and 
                // when the current one is null, up to the root.
                g = s.Generalization;
                var specRunning = s;
                while( g != null )
                {
                    var spec = g.FirstSpecialization;
                    while( spec != null )
                    {
                        if( spec != specRunning && !spec.Disabled ) spec.SetDisabled( ServiceDisabledReason.SiblingSpecializationRunning );
                        spec = spec.NextSpecialization;
                    }
                    PluginData p = g.FirstPlugin;
                    while( p != null )
                    {
                        if( !p.Disabled ) p.SetDisabled( PluginDisabledReason.ServiceSpecializationRunning );
                        p = p.NextPluginForService;
                    }
                    specRunning = g;
                    g = g.Generalization;
                    if( prevRunningService != null && g == prevRunningService.Generalization ) break;
                }
                return !s.Disabled;
            }

            internal void OnAllPluginsAdded()
            {
                if( !Root.Disabled )
                {
                    Root.OnAllPluginsAdded( s => _availableServices.Add( s ) );
                    Debug.Assert( Root.Disabled == (_availableServices.Count == 0) );
                }
            }

            public override string ToString()
            {
                return Root.ToString();
            }
        }

    }
}
