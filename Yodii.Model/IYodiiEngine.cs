﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CK.Core;
using System.ComponentModel;

namespace Yodii.Model
{


    public interface IYodiiEngine : INotifyPropertyChanged
    {
        IDiscoveredInfo DiscoveredInfo { get; set; }

        ConfigurationManager ConfigurationManager { get; }

        IYodiiEngineResult Start();

        void Stop();

        ILiveInfo LiveInfo { get; }

    }
}
