using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using Microsoft.Win32;

namespace SharpShell.ServerRegistration
{
    public class RegistryService : IRegistryService
    {
        private readonly bool asUser;
        private readonly RegistrationType type;
        public RegistryService(bool asUser, RegistrationType type)
        {
            this.type = type;
            this.asUser = asUser;
        }

        public override string ToString()
        {
            if (asUser)
            {
                return "Per-user " + type;
            }
            else
            {
                return "Per-machine " + type;
            }
        }

        public IRegistryKey OpenClassesRoot(bool fallback)
        {
            if (asUser && !fallback)
            {
                using (var user = OpenCurrentUser(type))
                {
                    return Wrap(user.OpenSubKey("SOFTWARE\\Classes", RegistryKeyPermissionCheck.ReadWriteSubTree));
                }
            } else
            { 
                return Wrap(OpenClassesRoot(type));
            }
        }

        public RegistrationType RegistrationType => type;

        public bool CanRead => true;

        public IRegistryKey OpenLocalMachineKey()
        {
            if (asUser) return null;
            return Wrap(RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,
               type == RegistrationType.OS64Bit ? RegistryView.Registry64 : RegistryView.Registry32));
        }
        
        private static RegistryKey OpenClassesRoot(RegistrationType registrationType)
        {
            return registrationType == RegistrationType.OS64Bit
                ? RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64) :
                  RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry32);
        }

        private static RegistryKey OpenCurrentUser(RegistrationType registrationType)
        {
            return registrationType == RegistrationType.OS64Bit
                ? RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64) :
                  RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32);
        }

        private static IRegistryKey Wrap(RegistryKey reg) => reg == null ? null : new RegistryKeyProxy(reg);

        private class RegistryKeyProxy : IRegistryKey
        {
            private readonly RegistryKey key;

            public RegistryKeyProxy(RegistryKey key)
            {
                this.key = key;
            }

            public void Dispose()
            {
                key.Dispose();
            }

            public IRegistryKey CreateSubKey(string keyName)
            {
                var subkey = key.CreateSubKey(keyName);
                return subkey == null ? null : new RegistryKeyProxy(subkey);
            }

            public IRegistryKey OpenSubKey(string keyName, RegistryKeyPermissionCheck permissions)
            {
                var subkey = key.OpenSubKey(keyName, permissions);
                return subkey == null ? null : new RegistryKeyProxy(subkey);
            }

            public void DeleteSubKeyTree(string subKeyTreeName)
            {
                key.DeleteSubKeyTree(subKeyTreeName);
            }

            public void DeleteValue(string name, bool throwIfNotExists)
            {
                key.DeleteValue(name, throwIfNotExists);
            }

            public IEnumerable<string> GetSubKeyNames()
            {
                return key.GetSubKeyNames();
            }

            public IEnumerable<string> GetValueNames()
            {
                return key.GetValueNames();
            }

            public T GetValue<T>(string name, T defaultValue)
            {
                return (T)key.GetValue(name, defaultValue);
            }

            public void SetValue(string name, string value, RegistryValueKind kind = RegistryValueKind.String)
            {
                key.SetValue(name, value, kind);
            }
        }
    }
}
