using System;
using System.Linq;
using System.Reflection;
using SharpShell.ServerRegistration;

namespace SharpShell.Attributes
{
    /// <summary>
    /// Identifies a function as being a static custom registration function.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class CustomUnregisterFunctionAttribute : Attribute
    {
        /// <summary>
        /// Executes the CustomUnregisterFunction if it exists for a type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="registry">The registry service used to write to the registry.</param>
        public static void ExecuteIfExists(Type type, IRegistryService registry)
        {
            ExecuteIfExists(type, registry, true);
        }

        /// <summary>
        /// Executes the CustomRegisterFunction if it exists for a type.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="registrationType">Type of the registration.</param>
        public static void ExecuteIfExists(Type type, RegistrationType registrationType)
        {
            ExecuteIfExists(type, ServerRegistrationManager.DefaultRegistryService(registrationType), false);
        }

        static void ExecuteIfExists(Type type, IRegistryService registry, bool throwOnLegacy)
        {
            //  Does the type have the attribute?
            var methodWithAttribute = type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy)
                .FirstOrDefault(m => m.GetCustomAttributes(typeof(CustomUnregisterFunctionAttribute), false).Any());

            //  Do we have a method? If so, invoke it.
            if (methodWithAttribute != null)
            {
                var param = methodWithAttribute.GetParameters();
                if (param.Length == 2)
                {
                    if (param[1].ParameterType.IsAssignableFrom(typeof(RegistrationType)))
                    {
                        if (registry.CanRead)
                        {
                            // Can only execute on a live registry
                            methodWithAttribute.Invoke(null, new object[] { type, registry.RegistrationType });
                        }
                        else if (throwOnLegacy)
                        {
                            throw new InvalidOperationException("Custom registration not supported by " + registry);
                        }
                    }
                    else if (param[1].ParameterType.IsAssignableFrom(typeof(IRegistryService)))
                    {
                        methodWithAttribute.Invoke(null, new object[] { type, registry });
                    }
                }
            }
        }
    }
}
