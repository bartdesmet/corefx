// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Xunit;

namespace System.Linq.Expressions.Tests
{
    public static class CompilerTests
    {
        [Theory]
        [ClassData(typeof(CompilationTypes))]
        [OuterLoop("Takes over a minute to complete")]
        public static void CompileDeepTree_NoStackOverflow(bool useInterpreter)
        {
            var e = (Expression)Expression.Constant(0);

            var n = 10000;

            for (var i = 0; i < n; i++)
                e = Expression.Add(e, Expression.Constant(1));

            var f = Expression.Lambda<Func<int>>(e).Compile(useInterpreter);

            Assert.Equal(n, f());
        }

#if FEATURE_COMPILE
        [Fact]
        public static void EmitConstantsToIL_NonNullableValueTypes()
        {
            VerifyEmitConstantsToIL((bool)true);

            VerifyEmitConstantsToIL((char)'a');

            VerifyEmitConstantsToIL((sbyte)42);
            VerifyEmitConstantsToIL((byte)42);
            VerifyEmitConstantsToIL((short)42);
            VerifyEmitConstantsToIL((ushort)42);
            VerifyEmitConstantsToIL((int)42);
            VerifyEmitConstantsToIL((uint)42);
            VerifyEmitConstantsToIL((long)42);
            VerifyEmitConstantsToIL((ulong)42);

            VerifyEmitConstantsToIL((float)3.14);
            VerifyEmitConstantsToIL((double)3.14);
            VerifyEmitConstantsToIL((decimal)49.95m);
        }

        [Fact]
        public static void EmitConstantsToIL_NullableValueTypes()
        {
            VerifyEmitConstantsToIL((bool?)null);
            VerifyEmitConstantsToIL((bool?)true);

            VerifyEmitConstantsToIL((char?)null);
            VerifyEmitConstantsToIL((char?)'a');

            VerifyEmitConstantsToIL((sbyte?)null);
            VerifyEmitConstantsToIL((sbyte?)42);
            VerifyEmitConstantsToIL((byte?)null);
            VerifyEmitConstantsToIL((byte?)42);
            VerifyEmitConstantsToIL((short?)null);
            VerifyEmitConstantsToIL((short?)42);
            VerifyEmitConstantsToIL((ushort?)null);
            VerifyEmitConstantsToIL((ushort?)42);
            VerifyEmitConstantsToIL((int?)null);
            VerifyEmitConstantsToIL((int?)42);
            VerifyEmitConstantsToIL((uint?)null);
            VerifyEmitConstantsToIL((uint?)42);
            VerifyEmitConstantsToIL((long?)null);
            VerifyEmitConstantsToIL((long?)42);
            VerifyEmitConstantsToIL((ulong?)null);
            VerifyEmitConstantsToIL((ulong?)42);

            VerifyEmitConstantsToIL((float?)null);
            VerifyEmitConstantsToIL((float?)3.14);
            VerifyEmitConstantsToIL((double?)null);
            VerifyEmitConstantsToIL((double?)3.14);
            VerifyEmitConstantsToIL((decimal?)null);
            VerifyEmitConstantsToIL((decimal?)49.95m);

            VerifyEmitConstantsToIL((DateTime?)null);
        }

        [Fact]
        public static void EmitConstantsToIL_ReferenceTypes()
        {
            VerifyEmitConstantsToIL((string)null);
            VerifyEmitConstantsToIL((string)"bar");
        }

        [Fact]
        public static void EmitConstantsToIL_Enums()
        {
            VerifyEmitConstantsToIL(ConstantsEnum.A);
            VerifyEmitConstantsToIL((ConstantsEnum?)null);
            VerifyEmitConstantsToIL((ConstantsEnum?)ConstantsEnum.A);
        }

        [Fact]
        public static void EmitConstantsToIL_ShareReferences()
        {
            var o = new object();
            VerifyEmitConstantsToIL(Expression.Equal(Expression.Constant(o), Expression.Constant(o)), 1, true);
        }

        [Fact]
        public static void EmitConstantsToIL_LiftedToClosure()
        {
            VerifyEmitConstantsToIL(DateTime.Now, 1);
            VerifyEmitConstantsToIL((DateTime?)DateTime.Now, 1);
        }

        [Fact]
        public static void VariableBinder_CatchBlock_Filter1()
        {
            // See https://github.com/dotnet/corefx/issues/11994 for reported issue

            Verify_VariableBinder_CatchBlock_Filter(
                Expression.Catch(
                    Expression.Parameter(typeof(Exception), "ex"),
                    Expression.Empty(),
                    Expression.Parameter(typeof(bool), "???")
                )
            );
        }

        [Fact]
        public static void VariableBinder_CatchBlock_Filter2()
        {
            // See https://github.com/dotnet/corefx/issues/11994 for reported issue

            Verify_VariableBinder_CatchBlock_Filter(
                Expression.Catch(
                    typeof(Exception),
                    Expression.Empty(),
                    Expression.Parameter(typeof(bool), "???")
                )
            );
        }

        [Fact]
        public static void VerifyIL_Simple()
        {
            Expression<Func<int>> f = () => Math.Abs(42);

            f.VerifyIL(
                @".method int32 ::lambda_method(object)
                  {
                    .maxstack 1

                    IL_0000: ldc.i4.s   42
                    IL_0002: call       int32 class [System.Private.CoreLib]System.Math::Abs(int32)
                    IL_0007: ret        
                  }");
        }

        [Fact]
        public static void VerifyIL_Exceptions()
        {
            ParameterExpression x = Expression.Parameter(typeof(int), "x");
            Expression<Func<int, int>> f =
                Expression.Lambda<Func<int, int>>(
                    Expression.TryCatchFinally(
                        Expression.Call(
                            typeof(Math).GetMethod(nameof(Math.Abs), new[] { typeof(int) }),
                            Expression.Divide(
                                Expression.Constant(42),
                                x
                            )
                        ),
                        Expression.Empty(),
                        Expression.Catch(
                            typeof(DivideByZeroException),
                            Expression.Constant(-1)
                        )
                    ),
                    x
                );

            f.VerifyIL(
                @".method int32 ::lambda_method(object,int32)
                  {
                    .maxstack 4
                    .locals init (
                      [0] int32
                    )
  
                    .try
                    {
                      .try
                      {
                          IL_0000: ldc.i4.s   42
                          IL_0002: ldarg.1    
                          IL_0003: div        
                          IL_0004: call       int32 class [System.Private.CoreLib]System.Math::Abs(int32)
                          IL_0009: stloc.0    
                          IL_000a: leave      IL_0017
                      }
                      catch (class [System.Private.CoreLib]System.DivideByZeroException)
                      {
                          IL_000f: pop        
                          IL_0010: ldc.i4.m1  
                          IL_0011: stloc.0    
                          IL_0012: leave      IL_0017
                      }
                      IL_0017: leave      IL_001d
                    }
                    finally
                    {
                      IL_001c: endfinally 
                    }
                    IL_001d: ldloc.0    
                    IL_001e: ret        
                  }");
        }

        [Fact]
        public static void VerifyIL_Closure1()
        {
            Expression<Func<Func<int>>> f = () => () => 42;

            f.VerifyIL(
                @".method class [System.Private.CoreLib]System.Func`1<int32> ::lambda_method(object)
                  {
                    .maxstack 3

                    IL_0000: ldarg.0    
                    IL_0001: castclass  class [System.Linq.Expressions]System.Runtime.CompilerServices.Closure`1<class [System.Private.CoreLib]System.Reflection.MethodInfo>
                    IL_0006: ldfld      class [System.Linq.Expressions]System.Runtime.CompilerServices.Closure`1<class [System.Private.CoreLib]System.Reflection.MethodInfo>::Item1
                    IL_000b: ldtoken    class [System.Private.CoreLib]System.Func`1<int32>
                    IL_0010: call       class [System.Private.CoreLib]System.Type class [System.Private.CoreLib]System.Type::GetTypeFromHandle(valuetype [System.Private.CoreLib]System.RuntimeTypeHandle)
                    IL_0015: ldnull     
                    IL_0016: callvirt   instance class [System.Private.CoreLib]System.Delegate class [System.Private.CoreLib]System.Reflection.MethodInfo::CreateDelegate(class [System.Private.CoreLib]System.Type,object)
                    IL_001b: castclass  class [System.Private.CoreLib]System.Func`1<int32>
                    IL_0020: ret        
                  }

                  // closure.Constants[0]
                  .method int32 ::lambda_method(object)
                  {
                    .maxstack 1

                    IL_0000: ldc.i4.s   42
                    IL_0002: ret        
                  }",
                appendInnerLambdas: true);
        }

        [Fact]
        public static void VerifyIL_Closure2()
        {
            Expression<Func<int, Func<int>>> f = x => () => x;

            f.VerifyIL(
                @".method class [System.Private.CoreLib]System.Func`1<int32> ::lambda_method(object,int32)
                  {
                    .maxstack 4
                    .locals init (
                      [0] class [System.Linq.Expressions.Tests]Unknown`1<int32>
                    )
                  
                    IL_0000: newobj     instance void class [System.Linq.Expressions]System.Runtime.CompilerServices.Closure`1<int32>::.ctor()
                    IL_0005: dup        
                    IL_0006: ldarg.1    
                    IL_0007: stfld      class [System.Linq.Expressions]System.Runtime.CompilerServices.Closure`1<int32>::Item1
                    IL_000c: stloc.0    
                    IL_000d: ldarg.0    
                    IL_000e: castclass  class [System.Linq.Expressions]System.Runtime.CompilerServices.Closure`1<class [System.Private.CoreLib]System.Reflection.MethodInfo>
                    IL_0013: ldfld      class [System.Linq.Expressions]System.Runtime.CompilerServices.Closure`1<class [System.Private.CoreLib]System.Reflection.MethodInfo>::Item1
                    IL_0018: ldtoken    class [System.Private.CoreLib]System.Func`1<int32>
                    IL_001d: call       class [System.Private.CoreLib]System.Type class [System.Private.CoreLib]System.Type::GetTypeFromHandle(valuetype [System.Private.CoreLib]System.RuntimeTypeHandle)
                    IL_0022: ldloc.0    
                    IL_0023: callvirt   instance class [System.Private.CoreLib]System.Delegate class [System.Private.CoreLib]System.Reflection.MethodInfo::CreateDelegate(class [System.Private.CoreLib]System.Type,object)
                    IL_0028: castclass  class [System.Private.CoreLib]System.Func`1<int32>
                    IL_002d: ret        
                  }
                  
                  // closure.Constants[0]
                  .method int32 ::lambda_method(object)
                  {
                    .maxstack 1
                    .locals init (
                      [0] class [System.Linq.Expressions.Tests]Unknown`1<int32>
                    )
                  
                    IL_0000: ldarg.0    
                    IL_0001: castclass  class [System.Linq.Expressions]System.Runtime.CompilerServices.Closure`1<int32>
                    IL_0006: stloc.0    
                    IL_0007: ldloc.0    
                    IL_0008: ldfld      class [System.Linq.Expressions]System.Runtime.CompilerServices.Closure`1<int32>::Item1
                    IL_000d: ret        
                  }",
                appendInnerLambdas: true);
        }

        [Fact]
        public static void VerifyIL_Closure3()
        {
            Expression<Func<int, Func<int, int>>> f = x => y => x + y;

            f.VerifyIL(
                @".method class [System.Private.CoreLib]System.Func`2<int32,int32> ::lambda_method(object,int32)
                  {
                    .maxstack 4
                    .locals init (
                      [0] class [System.Linq.Expressions.Tests]Unknown`1<int32>
                    )
                  
                    IL_0000: newobj     instance void class [System.Linq.Expressions]System.Runtime.CompilerServices.Closure`1<int32>::.ctor()
                    IL_0005: dup        
                    IL_0006: ldarg.1    
                    IL_0007: stfld      class [System.Linq.Expressions]System.Runtime.CompilerServices.Closure`1<int32>::Item1
                    IL_000c: stloc.0    
                    IL_000d: ldarg.0    
                    IL_000e: castclass  class [System.Linq.Expressions]System.Runtime.CompilerServices.Closure`1<class [System.Private.CoreLib]System.Reflection.MethodInfo>
                    IL_0013: ldfld      class [System.Linq.Expressions]System.Runtime.CompilerServices.Closure`1<class [System.Private.CoreLib]System.Reflection.MethodInfo>::Item1
                    IL_0018: ldtoken    class [System.Private.CoreLib]System.Func`2<int32,int32>
                    IL_001d: call       class [System.Private.CoreLib]System.Type class [System.Private.CoreLib]System.Type::GetTypeFromHandle(valuetype [System.Private.CoreLib]System.RuntimeTypeHandle)
                    IL_0022: ldloc.0    
                    IL_0023: callvirt   instance class [System.Private.CoreLib]System.Delegate class [System.Private.CoreLib]System.Reflection.MethodInfo::CreateDelegate(class [System.Private.CoreLib]System.Type,object)
                    IL_0028: castclass  class [System.Private.CoreLib]System.Func`2<int32,int32>
                    IL_002d: ret        
                  }
                  
                  // closure.Constants[0]
                  .method int32 ::lambda_method(object,int32)
                  {
                    .maxstack 2
                    .locals init (
                      [0] class [System.Linq.Expressions.Tests]Unknown`1<int32>
                    )
                  
                    IL_0000: ldarg.0    
                    IL_0001: castclass  class [System.Linq.Expressions]System.Runtime.CompilerServices.Closure`1<int32>
                    IL_0006: stloc.0    
                    IL_0007: ldloc.0    
                    IL_0008: ldfld      class [System.Linq.Expressions]System.Runtime.CompilerServices.Closure`1<int32>::Item1
                    IL_000d: ldarg.1    
                    IL_000e: add        
                    IL_000f: ret        
                  }",
                appendInnerLambdas: true);
        }

        public static void VerifyIL(this LambdaExpression expression, string expected, bool appendInnerLambdas = false)
        {
            var actual = expression.GetIL(appendInnerLambdas);

            var nExpected = Normalize(expected);
            var nActual = Normalize(actual);

            Assert.Equal(nExpected, nActual);
        }

        private static string Normalize(string s)
        {
            var lines =
                s
                .Replace("\r\n", "\n")
                .Split(new[] { '\n' })
                .Select(line => line.Trim())
                .Where(line => line != "" && !line.StartsWith("//"));

            return string.Join("\n", lines);
        }

        private static void VerifyEmitConstantsToIL<T>(T value)
        {
            VerifyEmitConstantsToIL<T>(value, 0);
        }

        private static void VerifyEmitConstantsToIL<T>(T value, int expectedCount)
        {
            VerifyEmitConstantsToIL(Expression.Constant(value, typeof(T)), expectedCount, value);
        }

        private static void VerifyEmitConstantsToIL(Expression e, int expectedCount, object expectedValue)
        {
            var d = Expression.Lambda(e).Compile();

            var t = d.Target;

            if (expectedCount == 0)
            {
                Assert.Null(t);
            }
            else
            {
                var c = t as IRuntimeVariables;

                if (c == null)
                {
                    var f = t.GetType().GetField("Constants");
                    Assert.NotNull(f);

                    var v = f.GetValue(t);

                    c = v as IRuntimeVariables;
                }

                Assert.NotNull(c);

                Assert.Equal(expectedCount, c.Count);
            }

            var o = d.DynamicInvoke();
            Assert.Equal(expectedValue, o);
        }

        private static void Verify_VariableBinder_CatchBlock_Filter(CatchBlock @catch)
        {
            var e =
                Expression.Lambda<Action>(
                    Expression.TryCatch(
                        Expression.Empty(),
                        @catch
                    )
                );

            Assert.Throws<InvalidOperationException>(() => e.Compile());
        }
#endif

        [Fact]
        public static void LambdaCompiler_Closures_Basic1()
        {
            Expression<Func<int, Func<int, int>>> f = x => y => x + y;

            var d = f.Compile();

            var e = d(1);

            Assert.Equal(3, e(2));
            Assert.Equal(4, e(3));
        }

        [Fact]
        public static void LambdaCompiler_Closures_Basic2()
        {
            Expression<Func<int, Func<int, Func<int, int>>>> f = x => y => z => x + y + z;

            var d = f.Compile();

            var e1 = d(1);
            var e2 = e1(2);

            Assert.Equal(6, e2(3));
            Assert.Equal(7, e2(4));
        }

        [Fact]
        public static void LambdaCompiler_Closures_Basic3()
        {
            Expression<Func<int, int, int, Func<int, int, int>>> f = (a, b, c) => (d, e) => a + b + c + d + e;

            var x = f.Compile();

            var y = x(1, 2, 3);

            Assert.Equal(15, y(4, 5));
            Assert.Equal(17, y(5, 6));
        }

        [Fact]
        public static void LambdaCompiler_Closures_Basic4()
        {
            var x = Expression.Parameter(typeof(int));
            var v = Expression.Parameter(typeof(int));

            var b =
                Expression.Block(
                    new[] { x },
                    Expression.New(
                        typeof(Box<int>).GetConstructors()[0],
                        Expression.Lambda<Func<int>>(
                            x
                        ),
                        Expression.Lambda<Action<int>>(
                            Expression.Assign(x, v),
                            v
                        )
                    )
                );

            var e = Expression.Lambda<Func<Box<int>>>(b);

            var c = e.Compile();

            var z = c();

            Assert.Equal(0, z.Value);

            z.Value = 1;
            Assert.Equal(1, z.Value);

            z.Value = 2;
            Assert.Equal(2, z.Value);
        }

        [Fact]
        public static void LambdaCompiler_Closures_NestedClosures()
        {
            Expression<Func<bool, byte, sbyte, char, short, ushort, int, uint, Func<long, ulong, float, double, decimal, string, Func<DateTime, TimeSpan, Guid, object, Func<object[]>>>>> f =
                (p0, p1, p2, p3, p4, p5, p6, p7) =>
                    (p8, p9, p10, p11, p12, p13) =>
                        (p14, p15, p16, p17) =>
                            () =>
                                new object[] { p0, p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13, p14, p15, p16, p17 };

            bool a0 = true;
            byte a1 = 123;
            sbyte a2 = 42;
            char a3 = 'c';
            short a4 = -567;
            ushort a5 = 987;
            int a6 = -1234;
            uint a7 = 9876;
            long a8 = 9876;
            ulong a9 = 9876;
            float a10 = (float)Math.PI;
            double a11 = Math.E;
            decimal a12 = 49.95m;
            string a13 = "bar";
            DateTime a14 = new DateTime(1983, 2, 11);
            TimeSpan a15 = TimeSpan.FromHours(12);
            Guid a16 = new Guid("A3B0694B-D78F-4B0A-9D87-14F850B11D42");
            object a17 = new object();

            var args = new object[] { a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17 };

            var d = f.Compile();

            var r = d(a0, a1, a2, a3, a4, a5, a6, a7)(a8, a9, a10, a11, a12, a13)(a14, a15, a16, a17)();

            Assert.Equal(args, r);
        }

        [Fact]
        public static void LambdaCompiler_Closures_BigClosure()
        {
            bool a0 = true;
            byte a1 = 123;
            sbyte a2 = 42;
            char a3 = 'c';
            short a4 = -567;
            ushort a5 = 987;
            int a6 = -1234;
            uint a7 = 9876;
            long a8 = 9876;
            ulong a9 = 9876;
            float a10 = (float)Math.PI;
            double a11 = Math.E;
            decimal a12 = 49.95m;
            string a13 = "bar";
            DateTime a14 = new DateTime(1983, 2, 11);
            TimeSpan a15 = TimeSpan.FromHours(12);
            Guid a16 = new Guid("A3B0694B-D78F-4B0A-9D87-14F850B11D42");
            object a17 = new object();

            var args = new object[] { a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, a10, a11, a12, a13, a14, a15, a16, a17 };

            var locals = args.Select(a => Expression.Parameter(a.GetType())).ToArray();

            Expression<Func<Func<object[]>>> f =
                Expression.Lambda<Func<Func<object[]>>>(
                    Expression.Block(
                        locals,
                        locals.Zip(args, (l, a) => Expression.Assign(l, Expression.Constant(a))).Cast<Expression>().Concat(new Expression[]
                        {
                            Expression.Lambda<Func<object[]>>(
                                Expression.NewArrayInit(
                                    typeof(object),
                                    locals.Select(p => Expression.Convert(p, typeof(object)))
                                )
                            )
                        })
                    )
                );

            var d = f.Compile();

            var r = d()();

            Assert.Equal(args, r);
        }

        [Fact]
        public static void LambdaCompiler_Closures_QuoteExposesStrongBox()
        {
            Expression<Action<int>> f = x => AssertQuoteExposesStrongBox(() => x);

            var d = f.Compile();

            d(42);
        }

        private static void AssertQuoteExposesStrongBox(Expression<Func<int>> e)
        {
            var m = e.Body as MemberExpression;
            Assert.NotNull(m);

            var f = m.Member as FieldInfo;
            Assert.NotNull(f);
            Assert.Equal("Value", f.Name);
            Assert.Equal(typeof(StrongBox<int>), f.DeclaringType);

            var c = m.Expression as ConstantExpression;
            Assert.NotNull(c);

            var b = c.Value as StrongBox<int>;
            Assert.Equal(42, b.Value);
        }

        [Fact]
        public static void LambdaCompiler_Closures_QuoteStrongBoxMutation()
        {
            Expression<Func<int, int>> f = x => AssertQuoteStrongBoxMutation(() => x) ? x : -1;

            var d = f.Compile();

            Assert.Equal(43, d(42));
        }

        private static bool AssertQuoteStrongBoxMutation(Expression<Func<int>> e)
        {
            var m = e.Body as MemberExpression;
            Assert.NotNull(m);

            var f = m.Member as FieldInfo;
            Assert.NotNull(f);
            Assert.Equal("Value", f.Name);
            Assert.Equal(typeof(StrongBox<int>), f.DeclaringType);

            var c = m.Expression as ConstantExpression;
            Assert.NotNull(c);

            var b = c.Value as StrongBox<int>;
            Assert.Equal(42, b.Value);

            b.Value = 43;

            return true;
        }

        [Fact]
        public static void LambdaCompiler_Closures_QuoteCompilation()
        {
            Expression<Func<int, int>> f = x => AssertQuoteCompilation(() => x);

            var d = f.Compile();

            Assert.Equal(42, d(42));
        }

        private static int AssertQuoteCompilation(Expression<Func<int>> e)
        {
            return e.Compile()();
        }

        [Fact]
        public static void LambdaCompiler_Closures_QuoteCompilationWithMutation()
        {
            Expression<Func<int, int[]>> f = x => InvokeN(CompileQuote(() => Interlocked.Increment(ref x)), 2);

            var d = f.Compile();

            var r = d(42);

            Assert.Equal(43, r[0]);
            Assert.Equal(44, r[1]);
        }

        private static Func<int> CompileQuote(Expression<Func<int>> e)
        {
            return e.Compile();
        }

        private static int[] InvokeN(Func<int> f, int n)
        {
            var res = new int[n];

            for (var i = 0; i < n; i++)
            {
                res[i] = f();
            }

            return res;
        }

        [Fact]
        public static void LambdaCompiler_Closures_QuotesAliasHoistedLocals()
        {
            Expression<Action<int>> f = x => AssertQuotesAliasHoistedLocals(() => x, () => x);

            var d = f.Compile();

            d(42);
        }

        private static void AssertQuotesAliasHoistedLocals(Expression<Func<int>> e1, Expression<Func<int>> e2)
        {
            Assert.Equal(42, e1.Compile()());
            Assert.Equal(42, e2.Compile()());

            var b1 = ((ConstantExpression)((MemberExpression)e1.Body).Expression).Value;
            var b2 = ((ConstantExpression)((MemberExpression)e2.Body).Expression).Value;
            Assert.Same(b1, b2);
        }

        [Fact]
        public static void LambdaCompiler_Closures_QuoteCanOutliveLambda()
        {
            Expression<Func<int, Expression<Func<int>>>> f = x => Return(() => x);

            var d = f.Compile();

            var e = d(42);

            Assert.Equal(42, e.Compile()());
        }

        private static Expression<Func<int>> Return(Expression<Func<int>> e)
        {
            return e;
        }

        [Fact]
        public static void LambdaCompiler_Closures_RuntimeVariablesReadWrite()
        {
            var x = Expression.Parameter(typeof(bool));
            var y = Expression.Parameter(typeof(int));
            var z = Expression.Parameter(typeof(string));

            var f =
                Expression.Lambda<Func<bool, int, string, int>>(
                    Expression.Block(
                        Expression.Call(
                            typeof(CompilerTests).GetMethod(nameof(AssertRuntimeVariablesReadWrite), BindingFlags.NonPublic | BindingFlags.Static),
                            Expression.RuntimeVariables(z, x, y),
                            x,
                            y,
                            z
                        ),
                        Expression.Condition(
                            x,
                            y,
                            Expression.Property(z, "Length")
                        )
                    ),
                    x, y, z
                );

            var d = f.Compile();

            d(true, 42, "bar");
        }

        private static void AssertRuntimeVariablesReadWrite(IRuntimeVariables vars, ref bool x, ref int y, ref string z)
        {
            Assert.Equal(true, x);
            Assert.Equal(true, vars[1]);

            Assert.Equal(42, y);
            Assert.Equal(42, vars[2]);

            Assert.Equal("bar", z);
            Assert.Equal("bar", vars[0]);

            vars[1] = false;
            vars[2] = 43;
            vars[0] = "foobar";

            Assert.Equal(false, x);
            Assert.Equal(false, vars[1]);

            Assert.Equal(43, y);
            Assert.Equal(43, vars[2]);

            Assert.Equal("foobar", z);
            Assert.Equal("foobar", vars[0]);
        }
    }

    public enum ConstantsEnum
    {
        A
    }

    class Box<T>
    {
        private readonly Func<T> _get;
        private readonly Action<T> _set;

        public Box(Func<T> get, Action<T> set)
        {
            _get = get;
            _set = set;
        }

        public T Value
        {
            get { return _get(); }
            set { _set(value); }
        }
    }
}
