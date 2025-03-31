/* Copyright (C) 2022 Mandiant, Inc. All Rights Reserved. */

using System.Collections.Generic;
using System;
using System.Text.RegularExpressions;
using dnlib.DotNet;
using RouteSixtySink.Outputters;

namespace RouteSixtySink.Helpers
{
    public static class MethodHelper
    {
        public static void GetPublicMethods(TypeDef type)
        {
            foreach (MethodDef method in type.Methods)
            {
                string methodAccess = method.Access.ToString();
                if (methodAccess == "Public" && !method.IsGetter && !method.IsSetter && (method.Parameters.Count > 0))
                {
                    List<string> parameterList = new();
                    foreach (var parameter in method.Parameters)
                    {
                        if (parameter.ToString().StartsWith("A_")) { continue; }
                        parameterList.Add(parameter.ToString());
                    }
                    if (parameterList.Count > 0)
                    {
                        Logger.Debug(String.Format("\tMethod: {0}::{1}\t\t{2}", type.ToString(), method.Name, String.Join(", ", parameterList)));
                    }
                }
            }
        }
        public static void GetMethods(TypeDef type)
        {
            foreach (MethodDef method in type.Methods)
            {
                Logger.Debug(String.Format("\tMethod: {0}::{1}", type.ToString(), method.Name));
            }
        }

        private const string GENERIC_METHOD_SIGNATURE = "`1";
        
        public static MethodDef GetMethod(TypeDef type, string methodName)
        {
            var searchForMethod = methodName;
            
            if (methodName.Contains(GENERIC_METHOD_SIGNATURE))
            {
                var newClassCache = _genericMethodNameCache.TryGetValue(type.FullName, out var methodCache);
                if (!newClassCache)
                {
                    methodCache = new();
                    _genericMethodNameCache.Add(type.FullName, methodCache);
                    foreach (var method in type.Methods)
                    {
                        if (!method.HasGenericParameters) continue;
                        
                        var genericMethodName = method.FullName
                            .Replace(".", @"\.")
                            .Replace("(", @"\(")
                            .Replace(")", @"\)")
                            .Replace("[", @"\[")
                            .Replace("]", @"\]");
                            
                        for (var paramIndex = 0; paramIndex < method.GenericParameters.Count; paramIndex++)
                        {
                            genericMethodName = Regex.Replace(genericMethodName, method.GenericParameters[paramIndex].FullName, @$"!!{paramIndex}");
                        }
                            
                        methodCache.Add(genericMethodName, method);
                    }
                }

                foreach (var method in methodCache)
                {
                    if (Regex.IsMatch(methodName, method.Key))
                    {
                        return method.Value;
                    }
                }
                
                if (type.HasInterfaces && TryGetInterfaceCallForMethod(methodName, out var interfaceCall))
                {
                    foreach (var interfaceImpl in type.Interfaces)
                    {
                        if (interfaceImpl.Interface.ScopeType.FullName == interfaceCall.InterfaceReflectedType)
                        {
                            var interfaceTypeSig = interfaceImpl.Interface.TryGetGenericInstSig();
                            if (interfaceTypeSig is not null && interfaceTypeSig.ContainsGenericParameter)
                            {
                                var interfaceDef = interfaceTypeSig.GenericType.TypeDef;
                                foreach (var interfaceMethod in interfaceDef.Methods)
                                {
                                    if (interfaceMethod.Name == interfaceCall.MethodName)
                                    {
                                        var methodFullName = interfaceMethod.FullName.Replace($"{interfaceTypeSig.GenericType.ReflectionFullName}::{interfaceMethod.Name}", $"{type.FullName}::{interfaceMethod.Name}");
                                        for (int argIndex = 0; argIndex < interfaceTypeSig.GenericArguments.Count; argIndex++)
                                        {
                                            methodFullName = Regex.Replace(methodFullName, @$"([<,]\s*){interfaceDef.GenericParameters[argIndex].FullName}(\s*[,>])", $"$1{interfaceTypeSig.GenericArguments[argIndex].FullName}$2");
                                        }

                                        foreach (MethodDef method in type.Methods)
                                        {
                                            if (method.FullName == methodFullName)
                                            {
                                                return method;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            foreach (MethodDef method in type.Methods)
            {
                if (method.FullName == methodName)
                {
                    return method;
                }
            }
            return null;
        }

        private static readonly Dictionary<string, Dictionary<string, MethodDef>> _genericMethodNameCache = new();

        private static bool TryGetInterfaceCallForMethod(string methodName, out InterfaceCall interfaceCall)
        {
            const string INTERFACE_REFLECTED_TYPE = "INTERFACE_REFLECTED_TYPE";
            const string INTERFACE_METHOD = "INTERFACE_METHOD";
            const string interfacePattern = $@"(::(?<{INTERFACE_REFLECTED_TYPE}>.+)\.(?<INTERFACE_METHOD>.+)\()";

            var match = Regex.Match(methodName, interfacePattern);
            interfaceCall = match.Success 
                ? new(match.Groups[INTERFACE_REFLECTED_TYPE].Value, match.Groups[INTERFACE_METHOD].Value) 
                : null;

            return match.Success;
        }

        private record InterfaceCall(string InterfaceReflectedType, string MethodName);
    }
}