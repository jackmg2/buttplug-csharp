﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Buttplug.Core;
using Buttplug.Devices.Protocols;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Buttplug.Devices.Configuration
{
    public class DeviceConfigurationManager
    {
        private static DeviceConfigurationManager _manager;

        public static DeviceConfigurationManager Manager
        {
            get
            {
                if (_manager == null)
                {
                    throw new NullReferenceException("Must load manager from file or resource before using!");
                }

                return _manager;
            }
        }

        // Types and Configurations are kept in separate dictionaries, as there may be cases where we
        // have a protocol but no matching configuration, or vice versa.
        protected readonly Dictionary<string, Type> _protocolTypes = new Dictionary<string, Type>();
        protected readonly Dictionary<string, List<IProtocolConfiguration>> _protocolConfigs = new Dictionary<string, List<IProtocolConfiguration>>();

        protected readonly List<IProtocolConfiguration> _whiteList = new List<IProtocolConfiguration>();
        protected readonly List<IProtocolConfiguration> _blackList = new List<IProtocolConfiguration>();

        public static bool HasManager => _manager != null;

        protected DeviceConfigurationManager()
        {
            // Names in this list must match the device config file keys.
            AddProtocol("lovense", typeof(LovenseProtocol));
            AddProtocol("kiiroo-v2", typeof(KiirooGen2Protocol));
            AddProtocol("cueme", typeof(CuemeProtocol));
            AddProtocol("kiiroo-v1", typeof(KiirooGen1Protocol));
            AddProtocol("kiiroo-v2-vibrator", typeof(KiirooGen2VibeProtocol));
            AddProtocol("libo", typeof(LiBoProtocol));
            AddProtocol("magic-motion", typeof(MagicMotionProtocol));
            AddProtocol("mysteryvibe", typeof(MysteryVibeProtocol));
            AddProtocol("picobong", typeof(PicobongProtocol));
            AddProtocol("prettylove", typeof(PrettyLoveProtocol));
            AddProtocol("vibratissimo", typeof(VibratissimoProtocol));
            AddProtocol("vorze-sa", typeof(VorzeSAProtocol));
            AddProtocol("wevibe", typeof(WeVibeProtocol));
            AddProtocol("youcups", typeof(YoucupsProtocol));
            AddProtocol("vorze-cyclone-x", typeof(CycloneX10Protocol));
            AddProtocol("youou", typeof(YououProtocol));
            AddProtocol("kiiroo-v21", typeof(KiirooGen21Protocol));
            AddProtocol("realtouch", typeof(RealTouchProtocol));

            // ET312 turned off until deadlocks and caching are fixed. See #593, #594, #595
            // AddProtocol("erostek-et312", typeof(ErostekET312Protocol));

            // todo We need a way to be able to turn off xinput, in case the protocol is blacklisted?
            AddProtocol("xinput", typeof(XInputProtocol));
        }

        private void LoadOrAppendConfigurationObject(string aConfigFile, bool aCanAddProtocols = true)
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(new HyphenatedNamingConvention())
                .Build();
            var configObject = deserializer.Deserialize<DeviceConfigurationFile>(aConfigFile);
            foreach (var protocolDefinition in configObject.Protocols)
            {
                var protocolName = protocolDefinition.Key;
                var protocolInfo = protocolDefinition.Value;

                if (!aCanAddProtocols && !_protocolConfigs.ContainsKey(protocolName))
                {
                    throw new ButtplugDeviceException("Cannot add protocols in user configuration files.");
                }

                if (protocolInfo?.Btle != null)
                {
                    var btleProtocolConfig = new BluetoothLEProtocolConfiguration(protocolInfo.Btle);
                    AddProtocolConfig(protocolName, btleProtocolConfig);
                }

                if (protocolInfo?.Hid != null)
                {
                    var hidProtocolConfig = new HIDProtocolConfiguration(protocolInfo.Hid);
                    AddProtocolConfig(protocolName, hidProtocolConfig);
                }

                if (protocolInfo?.Serial != null)
                {
                    var serialProtocolConfig = new SerialProtocolConfiguration(protocolInfo.Serial);
                    AddProtocolConfig(protocolName, serialProtocolConfig);
                }

                if (protocolInfo?.Usb != null)
                {
                    var usbProtocolConfig = new USBProtocolConfiguration(protocolInfo.Usb);
                    AddProtocolConfig(protocolName, usbProtocolConfig);
                }
            }
        }

        /// <summary>
        /// Loads configuration file from the configuration packed with the library on compilation.
        /// </summary>
        public static void LoadBaseConfigurationFromResource()
        {
            _manager = new DeviceConfigurationManager();
            var deviceConfig = ButtplugUtils.GetStringFromFileResource("Buttplug.buttplug-device-config.yml");
            _manager.LoadOrAppendConfigurationObject(deviceConfig);
        }

        public static void LoadBaseConfigurationFile(string aFileName)
        {
            _manager = new DeviceConfigurationManager();
            var deviceConfig = File.ReadAllText(aFileName);
            _manager.LoadOrAppendConfigurationObject(deviceConfig);
        }

        /// <summary>
        /// Loads user configuration. We require a base configuration to be loaded first, as user
        /// configurations should only add on to that.
        /// </summary>
        /// <param name="aFileName">Path to the user configuration file.</param>
        public void LoadUserConfigurationFile(string aFileName)
        {
            var deviceConfig = File.ReadAllText(aFileName);
            LoadUserConfigurationString(deviceConfig);
        }

        public void LoadUserConfigurationString(string aConfigString)
        {
            LoadOrAppendConfigurationObject(aConfigString, false);
        }

        public void AddProtocol(string aProtocolName, Type aProtocolType)
        {
            _protocolTypes.Add(aProtocolName, aProtocolType);
        }

        public void AddProtocolConfig(string aProtocolName, IProtocolConfiguration aConfiguration)
        {
            if (!_protocolConfigs.ContainsKey(aProtocolName))
            {
                _protocolConfigs.Add(aProtocolName, new List<IProtocolConfiguration>());
            }

            if (_protocolConfigs[aProtocolName].Any())
            {
                var config = _protocolConfigs[aProtocolName].Find(aX => aX.GetType() == aConfiguration.GetType());
                config?.Merge(aConfiguration);
            }

            _protocolConfigs[aProtocolName].Add(aConfiguration);
        }

        public void AddWhitelist(IProtocolConfiguration aConfiguration)
        {
            _whiteList.Add(aConfiguration);
        }

        public void AddBlacklist(IProtocolConfiguration aConfiguration)
        {
            _blackList.Add(aConfiguration);
        }

        public IEnumerable<ButtplugDeviceFactory> GetAllFactoriesOfType<T>()
        where T : IProtocolConfiguration
        {
            var factories = new List<ButtplugDeviceFactory>();
            foreach (var protocolConfigs in _protocolConfigs)
            {
                foreach (var deviceConfig in protocolConfigs.Value)
                {
                    // todo we should probably log if we fail this check.
                    if (deviceConfig is T && _protocolTypes.ContainsKey(protocolConfigs.Key))
                    {
                        factories.Add(new ButtplugDeviceFactory(deviceConfig, _protocolTypes[protocolConfigs.Key]));
                    }
                }
            }

            return factories;
        }

        public ButtplugDeviceFactory Find(IProtocolConfiguration aConfig)
        {
            if (_whiteList.Any())
            {
                var found = false;
                foreach (var config in _whiteList)
                {
                    if (!aConfig.Matches(config))
                    {
                        continue;
                    }

                    found = true;
                    break;
                }

                // If we found a whitelisted device, continue on to figure out its type.
                if (!found)
                {
                    return null;
                }
            }

            if (_blackList.Any())
            {
                foreach (var config in _blackList)
                {
                    if (aConfig.Matches(config))
                    {
                        return null;
                    }
                }
            }

            foreach (var config in _protocolConfigs)
            {
                foreach (var deviceConfig in config.Value)
                {
                    if (!deviceConfig.Matches(aConfig))
                    {
                        continue;
                    }

                    if (!_protocolTypes.ContainsKey(config.Key))
                    {
                        // Todo This means we found a device we have config but no protocol for. We should log here and return null.
                        return null;
                    }

                    // We can't create the device just yet, as we need to let the subtype manager try
                    // to connect to the device and set it up appropriately. Return a device factory
                    // to let the subtype manager do that.
                    return new ButtplugDeviceFactory(deviceConfig, _protocolTypes[config.Key]);
                }
            }

            return null;
        }

        /// <summary>
        /// Clears the manager. Used for testing only.
        /// </summary>
        internal static void Clear()
        {
            _manager = null;
        }
    }
}
