// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using static System.Linq.Expressions.CachedReflectionInfo;

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
                value = fi.GetValue(obj: null);
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        public static bool IsStringSwitch(SwitchExpression node)
        {
            // If we have a comparison other than string equality, bail
            MethodInfo equality = String_op_Equality_String_String;
            if (equality != null && !equality.IsStatic)
            {
                equality = null;
            }

            if (node.Comparison != equality)
            {
                return false;
            }

            return true;
        }

        public static bool ShouldEmitHashtableSwitch(SwitchExpression node, out int numberOfTests)
        {
            numberOfTests = 0;

            if (!IsStringSwitch(node))
            {
                return false;
            }

            // All test values must be constant.
            foreach (SwitchCase c in node.Cases)
            {
                foreach (Expression t in c.TestValues)
                {
                    if (!(t is ConstantExpression))
                    {
                        return false;
                    }
                    numberOfTests++;
                }
            }

            // Must have >= 7 labels for it to be worth it.
            if (numberOfTests < 7)
            {
                return false;
            }

            return true;
        }

        public static bool IsStringHashtableSwitch(SwitchExpression node)
        {
            int ignored = 0;
            return ShouldEmitHashtableSwitch(node, out ignored);
        }
    }
}
