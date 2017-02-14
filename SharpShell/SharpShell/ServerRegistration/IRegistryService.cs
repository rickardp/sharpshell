using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using Microsoft.Win32;

namespace SharpShell.ServerRegistration
{
    public interface IRegistryService
    {
        IRegistryKey OpenClassesRoot(bool fallback = false);
        /// <summary>
        /// Opens the local machine root key. Returns null if not available.
        /// </summary>
        /// <returns></returns>
        IRegistryKey OpenLocalMachineKey();

        /// <summary>
        /// Opens the local machine or user key.
        /// </summary>
        /// <returns></returns>
        IRegistryKey OpenRootKey();

        bool CanRead { get; }
        RegistrationType RegistrationType { get; }
    }

    public interface IRegistryKey : IDisposable
    {
        IRegistryKey OpenSubKey(string keyName, RegistryKeyPermissionCheck permissions);
        IRegistryKey CreateSubKey(string keyName);
        void SetValue(string name, string value, RegistryValueKind kind = RegistryValueKind.String);
        IEnumerable<string> GetSubKeyNames();
        IEnumerable<string> GetValueNames();
        void DeleteSubKeyTree(string subKeyTreeName);
        T GetValue<T>(string name, T defaultValue);
        void DeleteValue(string name, bool throwOnMissing = true);
    }
}
