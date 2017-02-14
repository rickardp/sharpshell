using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using Microsoft.Win32;

namespace SharpShell.ServerRegistration
{
    public class RegFileService : IRegistryService, IDisposable
    {
        private readonly bool asUser;
        private readonly string path;
        private readonly Dictionary<string, Dictionary<string, string>> data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        public RegFileService(bool asUser, string path)
        {
            this.asUser = asUser;
            this.path = path;
        }

        public override string ToString()
        {
            return ".REG file";
        }

        public void Dispose()
        {
            if (data.Any())
            {
                using (var outputWriter = new StreamWriter(path, false, Encoding.Unicode))
                {
                    outputWriter.WriteLine(Header);
                    outputWriter.WriteLine();
                    outputWriter.WriteLine();
                    foreach (var kv in data.OrderBy(x=>x.Key))
                    {
                        outputWriter.Write("[");
                        outputWriter.Write(kv.Key);
                        outputWriter.WriteLine("]");
                        foreach (var ikv in kv.Value.OrderBy(x=>x.Key))
                        {
                            if(string.IsNullOrEmpty(ikv.Key))
                                outputWriter.Write("@");
                            else
                            {
                                outputWriter.Write("\"");
                                outputWriter.Write(ikv.Key);
                                outputWriter.Write("\"");
                            }
                            outputWriter.Write("=");
                            outputWriter.WriteLine(ikv.Value);
                        }
                        outputWriter.WriteLine();
                    }
                }
            }
        }

        public IRegistryKey OpenClassesRoot(bool fallback)
        {
            if (asUser)
            {
                return new RegistryKeyProxy(this, HKEY_CURRENT_USER).OpenSubKey("SOFTWARE\\Classes", RegistryKeyPermissionCheck.Default);
            }
            else
            {
                return new RegistryKeyProxy(this, HKEY_CLASSES_ROOT);
            }
        }

        public RegistrationType RegistrationType => RegistrationType.OS64Bit;

        public bool CanRead => false;

        public IRegistryKey OpenLocalMachineKey()
        {
            if (asUser) return null;
            return new RegistryKeyProxy(this, HKEY_LOCAL_MACHINE);
        }

        public IRegistryKey OpenRootKey()
        {
            return new RegistryKeyProxy(this, asUser ? HKEY_CURRENT_USER : HKEY_LOCAL_MACHINE);
        }

        private const string Header = "Windows Registry Editor Version 5.00";

        private const string HKEY_CURRENT_USER = nameof(HKEY_CURRENT_USER);
        private const string HKEY_LOCAL_MACHINE = nameof(HKEY_LOCAL_MACHINE);
        private const string HKEY_CLASSES_ROOT = nameof(HKEY_CLASSES_ROOT);

        private class RegistryKeyProxy : IRegistryKey
        {
            private readonly RegFileService parent;
            private readonly string path;

            public RegistryKeyProxy(RegFileService parent, string path)
            {
                this.parent = parent;
                this.path = path.TrimEnd('\\');
            }

            public void Dispose()
            {
            }

            public IRegistryKey CreateSubKey(string keyName)
            {

                var ok = (RegistryKeyProxy)OpenSubKey(keyName, RegistryKeyPermissionCheck.Default);
                ok.GetDict();
                return ok;
            }

            private Dictionary<string, string> GetDict()
            {
                Dictionary<string, string> rec;
                if (!parent.data.TryGetValue(path, out rec))
                {
                    rec = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    parent.data.Add(path, rec);
                }
                return rec;
            }

            public IRegistryKey OpenSubKey(string keyName, RegistryKeyPermissionCheck permissions)
            {
                return new RegistryKeyProxy(parent, path + "\\" + keyName);
            }

            public void DeleteSubKeyTree(string subKeyTreeName)
            {
                throw new NotImplementedException("Cannot delete keys in reg files");
            }

            public void DeleteValue(string name, bool throwIfNotExists)
            {
                SetValue(name, null);
            }

            public IEnumerable<string> GetSubKeyNames()
            {
                return Enumerable.Empty<string>();
            }

            public IEnumerable<string> GetValueNames()
            {
                return Enumerable.Empty<string>();
            }

            public T GetValue<T>(string name, T defaultValue)
            {
                throw new NotImplementedException("Cannot read from reg file");
            }

            public void SetValue(string name, string value, RegistryValueKind kind = RegistryValueKind.String)
            {
                if (string.IsNullOrEmpty(name))
                    name = "";
                GetDict()[name] = "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            }
        }
    }
}
