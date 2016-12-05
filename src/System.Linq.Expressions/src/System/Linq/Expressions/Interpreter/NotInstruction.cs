﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// Do not edit this file manually. This file is auto-generated by NotInstruction.tt.

using System.Diagnostics;
using System.Dynamic.Utils;
using System.Reflection;

namespace System.Linq.Expressions.Interpreter
{
    internal abstract class NotInstruction : Instruction
    {
        private static Instruction s_Boolean, s_SByte, s_Int16, s_Int32, s_Int64, s_Byte, s_UInt16, s_UInt32, s_UInt64;

        public override int ConsumedStack => 1;
        public override int ProducedStack => 1;
        public override string InstructionName => "Not";

        private NotInstruction() { }

        private sealed class NotBoolean : NotInstruction
        {
            public override int Run(InterpretedFrame frame)
            {
                object obj = frame.Pop();
                if (obj == null)
                {
                    frame.Push(null);
                }
                else
                {
                    frame.Push(!(bool)obj);
                }
                return 1;
            }
        }

        private sealed class NotSByte : NotInstruction
        {
            public override int Run(InterpretedFrame frame)
            {
                object obj = frame.Pop();
                if (obj == null)
                {
                    frame.Push(null);
                }
                else
                {
                    frame.Push((sbyte)(~(sbyte)obj));
                }
                return 1;
            }
        }

        private sealed class NotInt16 : NotInstruction
        {
            public override int Run(InterpretedFrame frame)
            {
                object obj = frame.Pop();
                if (obj == null)
                {
                    frame.Push(null);
                }
                else
                {
                    frame.Push((short)(~(short)obj));
                }
                return 1;
            }
        }

        private sealed class NotInt32 : NotInstruction
        {
            public override int Run(InterpretedFrame frame)
            {
                object obj = frame.Pop();
                if (obj == null)
                {
                    frame.Push(null);
                }
                else
                {
                    frame.Push(~(int)obj);
                }
                return 1;
            }
        }

        private sealed class NotInt64 : NotInstruction
        {
            public override int Run(InterpretedFrame frame)
            {
                object obj = frame.Pop();
                if (obj == null)
                {
                    frame.Push(null);
                }
                else
                {
                    frame.Push(~(long)obj);
                }
                return 1;
            }
        }

        private sealed class NotByte : NotInstruction
        {
            public override int Run(InterpretedFrame frame)
            {
                object obj = frame.Pop();
                if (obj == null)
                {
                    frame.Push(null);
                }
                else
                {
                    frame.Push((byte)(~(byte)obj));
                }
                return 1;
            }
        }

        private sealed class NotUInt16 : NotInstruction
        {
            public override int Run(InterpretedFrame frame)
            {
                object obj = frame.Pop();
                if (obj == null)
                {
                    frame.Push(null);
                }
                else
                {
                    frame.Push((ushort)(~(ushort)obj));
                }
                return 1;
            }
        }

        private sealed class NotUInt32 : NotInstruction
        {
            public override int Run(InterpretedFrame frame)
            {
                object obj = frame.Pop();
                if (obj == null)
                {
                    frame.Push(null);
                }
                else
                {
                    frame.Push(~(uint)obj);
                }
                return 1;
            }
        }

        private sealed class NotUInt64 : NotInstruction
        {
            public override int Run(InterpretedFrame frame)
            {
                object obj = frame.Pop();
                if (obj == null)
                {
                    frame.Push(null);
                }
                else
                {
                    frame.Push(~(ulong)obj);
                }
                return 1;
            }
        }

        public static Instruction Create(Type type)
        {
            Debug.Assert(!type.GetTypeInfo().IsEnum);
            switch (type.GetNonNullableType().GetTypeCode())
            {
                case TypeCode.Boolean: return s_Boolean ?? (s_Boolean = new NotBoolean());
                case TypeCode.SByte: return s_SByte ?? (s_SByte = new NotSByte());
                case TypeCode.Int16: return s_Int16 ?? (s_Int16 = new NotInt16());
                case TypeCode.Int32: return s_Int32 ?? (s_Int32 = new NotInt32());
                case TypeCode.Int64: return s_Int64 ?? (s_Int64 = new NotInt64());
                case TypeCode.Byte: return s_Byte ?? (s_Byte = new NotByte());
                case TypeCode.UInt16: return s_UInt16 ?? (s_UInt16 = new NotUInt16());
                case TypeCode.UInt32: return s_UInt32 ?? (s_UInt32 = new NotUInt32());
                case TypeCode.UInt64: return s_UInt64 ?? (s_UInt64 = new NotUInt64());
                default:
                    throw Error.ExpressionNotSupportedForType("Not", type);
            }
        }
    }
}
