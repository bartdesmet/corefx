// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Reflection;

namespace System.Linq.Expressions
{
    internal static class Utils
    {
        private static readonly DefaultExpression s_voidInstance = Expression.Empty();

        public static DefaultExpression Empty()
        {
            return s_voidInstance;
        }

        public static bool TryGetRawConstantValue(FieldInfo fi, out object value)
        {
            // TODO: It looks like GetRawConstantValue is not available at the moment, use it when it comes back.
            //value = fi.GetRawConstantValue();
            //return true;

            try
            {
                value = fi.GetValue(null);
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }
    }
}
