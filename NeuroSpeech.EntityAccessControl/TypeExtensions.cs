using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace NeuroSpeech.EntityAccessControl.Internal
{

    public static class EnumerableExtensions
    {

        internal static Type GetFirstGenericArgument(this Type type)
        {
            if (type.IsConstructedGenericType)
            {
                return type.GetGenericArguments()[0];
            }
            return type;
        }

        internal static Type GetSecondGenericArgument(this Type type)
        {
            if (type.IsConstructedGenericType)
            {
                return type.GetGenericArguments()[1];
            }
            return type;
        }

        internal static Type GetFuncReturnType(this Type type)
        {
            var itemType = type.GetFirstGenericArgument();
            return itemType.GetMethod("Invoke").ReturnType;
        }


        public static bool TryGetFirst<T,TCompare>(this IEnumerable<T> target, TCompare key, Func<T, TCompare, bool> fx, out T value)
        {
            foreach (var v in target)
            {
                if (fx(v, key))
                {
                    value = v;
                    return true;
                }
            }
            value = default!;
            return false;
        }

        public static bool TryGetFirst<T>(this IEnumerable<T> target, Func<T, bool> fx, out T value)
        {
            foreach (var v in target)
            {
                if (fx(v))
                {
                    value = v;
                    return true;
                }
            }
            value = default!;
            return false;
        }

    }

    public static class DictionaryExtensions
    {
        public static TValue GetOrCreate<TKey, TValue>(this IDictionary<TKey, TValue> d, TKey key, Func<TKey, TValue> factory)
        {
            if (d.TryGetValue(key, out var v))
                return v;
            d[key] = v = factory(key);
            return v;
        }
    }
    public static class TypeExtensions
    {

        public static Type LowerToEnumerable(this Type type)
        {
            if(!type.IsConstructedGenericType)
            {
                return type;
            }
            var gtd = type.GetGenericTypeDefinition();
            var lowered = typeof(IEnumerable<>).MakeGenericType(type.GenericTypeArguments[0]);
            if (lowered.IsAssignableFrom(gtd))
            {
                return lowered;
            }
            return type;
        }

        public static bool IsAnonymous(this Type type)
        {
            if (type == null)
                throw new ArgumentNullException("type");

            // HACK: The only way to detect anonymous types right now.
            return Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute), false)
                && type.IsGenericType && type.Name.Contains("AnonymousType")
                && (type.Name.StartsWith("<>") || type.Name.StartsWith("VB$"))
                && type.Attributes.HasFlag(TypeAttributes.NotPublic);
        }

        public static string GetJsonPropertyName(this PropertyInfo p, JsonNamingPolicy? policy = null)
        {
            return p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? (policy ?? JsonNamingPolicy.CamelCase).ConvertName(p.Name);
        }
        public static string GetJsonPropertyName(this IPropertyBase p, JsonNamingPolicy? policy = null)
        {
            return p.PropertyInfo?.GetJsonPropertyName(policy)
                ?? (policy ?? JsonNamingPolicy.CamelCase).ConvertName(p.Name);
        }
        public static MethodInfo GetStaticMethod(
            this Type type, 
            string name, 
            int argLength,
            Func<List<Type>,bool> filter)
        {
            var methods = type
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(x => x.Name == name && x.GetParameters().Length == argLength)
                .Select(x => (Method: x, Types: x.GetParameters().Select(v => v.ParameterType).ToList()))
                .ToList();
            var (first, _) = methods.FirstOrDefault((x) => filter(x.Types));
            return first ?? throw new KeyNotFoundException();
        }


        private static Dictionary<Type, object> defaults = new Dictionary<Type, object> {
        };

        private static object Boolean = false;
        private static object Char = (char)0;
        private static object SByte = (sbyte)0;
        private static object Byte = (byte)0;
        private static object Int16 = (short)0;
        private static object UInt16 = (ushort)0;
        private static object Int32 = 0;
        private static object UInt32 = (uint)0;
        private static object Int64 = (long)0;
        private static object UInt64 = (ulong)0;
        private static object Single = (float)0;
        private static object Double = (double)0;
        private static object Decimal = (decimal)0;
        private static object DateTime = System.DateTime.MinValue;
        private static object DateTimeOffset = System.DateTimeOffset.MinValue;

        public static object? GetDefaultForType(this Type type) {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (!type.IsValueType)
                return null;
            var tc = Type.GetTypeCode(type);
            switch (tc)
            {
                case TypeCode.Boolean:
                    return Boolean;
                case TypeCode.Char:
                    return Char;
                case TypeCode.SByte:
                    return SByte;
                case TypeCode.Byte:
                    return Byte;
                case TypeCode.Int16:
                    return Int16;
                case TypeCode.UInt16:
                    return UInt16;
                case TypeCode.Int32:
                    return Int32;
                case TypeCode.UInt32:
                    return UInt32;
                case TypeCode.Int64:
                    return Int64;
                case TypeCode.UInt64:
                    return UInt64;
                case TypeCode.Single:
                    return Single;
                case TypeCode.Double:
                    return Double;
                case TypeCode.Decimal:
                    return Decimal;
                case TypeCode.DateTime:
                    return DateTime;
            }
            if (type == typeof(System.DateTimeOffset))
                return DateTimeOffset;
            return defaults.GetOrCreate(type, x => Activator.CreateInstance(x)!);
        }

        public static string ToTypeScript(this Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type.IsEnum)
            {
                return string.Join(" | ", type.GetEnumNames().Select(x => $"\"{x}\""));
            }
            var tc = Type.GetTypeCode(type);
            switch (tc)
            {
                case TypeCode.Boolean:
                    return "boolean";
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                    return "number";
                case TypeCode.DateTime:
                    return "DateTime";
                case TypeCode.Char:
                case TypeCode.String:
                    return "string";
            }

            if (type == typeof(DateTimeOffset))
                return "DateTime";
            if (typeof(Geometry).IsAssignableFrom(type))
            {
                return "IGeometry";
            }
            return $"any /*{type.FullName}*/";
        }

        public static bool TryGetEnumerableItem(this Type type, out Type itemType )
        {
            var t = type.StaticCacheGetOrCreate("EN" + type.FullName, () =>
            {
                if (!(typeof(System.Collections.IEnumerable).IsAssignableFrom(type)))
                    return null;
                do
                {
                    var td = typeof(IEnumerable<>);
                    var tdc = typeof(ICollection<>);
                    foreach (var i in type.GetInterfaces())
                    {
                        if (i.IsConstructedGenericType)
                        {
                            if (i.GetGenericTypeDefinition() == td 
                            || i.GetGenericTypeDefinition() == tdc)
                            return i.GetGenericArguments()[0];
                        }
                    }
                    type = type.BaseType!;
                } while (type != null);
                return null;
            });
            itemType = t!;
            return t != null;
        }

        public static System.Collections.IList? GetOrCreateCollection(this PropertyInfo property, object target, Type itemType)
        {
            var value = property.GetValue(target);
            if (value == null)
            {
                value = property.PropertyType.CreateCollection(itemType)!;
                property.SetValue(target, value);
            }
            return value as System.Collections.IList;
        }

        private static ConcurrentDictionary<Type, Func<System.Collections.IList>> collectionFactories
            = new ConcurrentDictionary<Type, Func<System.Collections.IList>>();

        public static System.Collections.IList CreateCollection(this Type type, Type itemType)
        {
            var f = collectionFactories.GetOrAdd(type, x =>
            {
                if (type.IsInterface)
                {
                    var t = typeof(List<>).MakeGenericType(itemType);
                    if (type.IsAssignableFrom(t))
                    {
                        type = t;
                    } else
                    {
                        Func<System.Collections.IList> fx = () => throw new InvalidOperationException($"Cannot instantiate type {type.FullName}");
                        return fx;
                    }
                }
                var body = Expression.Lambda<Func<System.Collections.IList>>(
                    Expression.New(type));
                return body.Compile();
            });
            return f();
        }

        internal static bool IsForeignKey(this IModel model, PropertyInfo p)
        {
            return model.FindEntityType(p.DeclaringType)?.FindProperty(p)?.IsForeignKey() ?? false;
        }

    }

    //public static class DynamicHelper
    //{
    //    public class Method
    //    {
    //        private MethodInfo method;
    //        private object? @delegate;

    //        public Method(MethodInfo method)
    //        {
    //            this.method = method;
    //            @delegate = null;
    //        }

    //        internal TDelegate CreateDelegate<TDelegate>()
    //        {
    //            return (TDelegate)(@delegate ??= method.CreateDelegate(typeof(TDelegate)));
    //        }
    //    }

    //    public struct MethodWithTarget<RT>
    //    {
    //        private Method method;
    //        private object target;

    //        public MethodWithTarget(Method method, object target)
    //        {
    //            this.method = method;
    //            this.target = target;
    //        }

    //        public RT Invoke()
    //        {
    //            return method.CreateDelegate<Func<RT>>();
    //        }

    //    }

    //    public struct MethodWithTarget
    //    {
    //        private readonly Method method;
    //        private readonly object target;

    //        public MethodWithTarget(Method method, object target)
    //        {
    //            this.method = method;
    //            this.target = target;
    //        }

    //        public MethodWithTarget<RT> As<RT>() => new MethodWithTarget<RT>(method, target);

    //    }

    //}
    public static class Generic
    {
        private static ConcurrentDictionary<(Type type1, Type type2, MethodInfo method), object> cache
            = new ConcurrentDictionary<(Type, Type, MethodInfo), object>();

        private static T CreateTypedDelegate<T>(this MethodInfo method)
            where T : Delegate
        {
            return (T)method.CreateDelegate(typeof(T));
        }

        private static T TypedGet<T>(
            (Type, Type, MethodInfo) key,
            Func<(Type type1, Type type2, MethodInfo method), T> create)
        {
            return (T)cache.GetOrAdd(key, (x) => create(x));
        }

        public static T InvokeAs<T>(Type type, Func<T> fx)
        {
            var method = TypedGet(
                    (type, type, fx.Method),
                    (k) => k.method
                        .GetGenericMethodDefinition()
                        .MakeGenericMethod(k.type1)
                        .CreateTypedDelegate<Func<T>>());
            return method();
        }

        public static T InvokeAs<T1, T>(Type type, Func<T1, T> fx, T1 p1)
        {
            var method = TypedGet(
                    (type, type, fx.Method),
                    (k) => k.method
                        .GetGenericMethodDefinition()
                        .MakeGenericMethod(k.type1)
                        .CreateTypedDelegate<Func<T1, T>>());
            return method(p1);
        }

        public static T InvokeAs<T1, T2, T>(Type type, Func<T1, T2, T> fx, T1 p1, T2 p2)
        {
            var method = TypedGet(
                    (type, type, fx.Method),
                    (k) => k.method
                        .GetGenericMethodDefinition()
                        .MakeGenericMethod(k.type1)
                        .CreateTypedDelegate<Func<T1, T2, T>>());
            return method(p1, p2);
        }
        public static T InvokeAs<T1, T2, T3, T>(Type type, Func<T1, T2, T3, T> fx, T1 p1, T2 p2, T3 p3)
        {
            var method = TypedGet(
                    (type, type, fx.Method),
                    (k) => k.method
                        .GetGenericMethodDefinition()
                        .MakeGenericMethod(k.type1)
                        .CreateTypedDelegate<Func<T1, T2, T3, T>>());
            return method(p1, p2, p3);
        }

        public static T InvokeAs<T>(Type type, Type type2, Func<T> fx)
        {
            var method = TypedGet(
                    (type, type2, fx.Method),
                    (k) => k.method
                        .GetGenericMethodDefinition()
                        .MakeGenericMethod(k.type1, k.type2)
                        .CreateTypedDelegate<Func<T>>());
            return method();
        }

        public static T InvokeAs<T1, T>(Type type, Type type2, Func<T1, T> fx, T1 p1)
        {
            var method = TypedGet(
                    (type, type2, fx.Method),
                    (k) => k.method
                        .GetGenericMethodDefinition()
                        .MakeGenericMethod(k.type1, k.type2)
                        .CreateTypedDelegate<Func<T1, T>>());
            return method(p1);
        }

        public static T InvokeAs<T1, T2, T>(Type type, Type type2, Func<T1, T2, T> fx, T1 p1, T2 p2)
        {
            var method = TypedGet(
                    (type, type2, fx.Method),
                    (k) => k.method
                        .GetGenericMethodDefinition()
                        .MakeGenericMethod(k.type1, k.type2)
                        .CreateTypedDelegate<Func<T1, T2, T>>());
            return method(p1, p2);
        }
        public static T InvokeAs<T1, T2, T3, T>(Type type, Type type2, Func<T1, T2, T3, T> fx, T1 p1, T2 p2, T3 p3)
        {
            var method = TypedGet(
                    (type, type2, fx.Method),
                    (k) => k.method
                        .GetGenericMethodDefinition()
                        .MakeGenericMethod(k.type1, k.type2)
                        .CreateTypedDelegate<Func<T1, T2, T3, T>>());
            return method(p1, p2, p3);
        }

        public static T InvokeAs<T1, T2, T3, T4, T>(Type type, Type type2, Func<T1, T2, T3, T4, T> fx, T1 p1, T2 p2, T3 p3, T4 p4)
        {
            var method = TypedGet(
                    (type, type2, fx.Method),
                    (k) => k.method
                        .GetGenericMethodDefinition()
                        .MakeGenericMethod(k.type1, k.type2)
                        .CreateTypedDelegate<Func<T1, T2, T3, T4, T>>());
            return method(p1, p2, p3, p4);
        }

        public static T InvokeAs<Target, T>(this Target target, Type type, Func<T> fx)
        {
            var method = TypedGet(
                    (type, type, fx.Method),
                    (k) => k.method
                        .GetGenericMethodDefinition()
                        .MakeGenericMethod(k.type1)
                        .CreateTypedDelegate<Func<Target, T>>());
            return method(target);
        }

        public static T InvokeAs<Target, T1, T>(this Target target, Type type, Func<T1, T> fx, T1 p1)
        {
            var method = TypedGet(
                    (type, type, fx.Method),
                    (k) => k.method
                        .GetGenericMethodDefinition()
                        .MakeGenericMethod(k.type1)
                        .CreateTypedDelegate<Func<Target, T1, T>>());
            return method(target, p1);
        }

        public static T InvokeAs<Target, T1, T2, T>(this Target target, Type type, Func<T1, T2, T> fx, T1 p1, T2 p2)
        {
            var method = TypedGet(
                    (type, type, fx.Method),
                    (k) => k.method
                        .GetGenericMethodDefinition()
                        .MakeGenericMethod(k.type1)
                        .CreateTypedDelegate<Func<Target, T1, T2, T>>());
            return method(target, p1, p2);
        }
        public static T InvokeAs<Target, T1, T2, T3, T>(this Target target, Type type, Func<T1, T2, T3, T> fx, T1 p1, T2 p2, T3 p3)
        {
            var method = TypedGet(
                    (type, type, fx.Method),
                    (k) => k.method
                        .GetGenericMethodDefinition()
                        .MakeGenericMethod(k.type1)
                        .CreateTypedDelegate<Func<Target, T1, T2, T3, T>>());
            return method(target, p1, p2, p3);
        }

        public static T InvokeAs<Target, T>(this Target target, Type type, Type type2, Func<T> fx)
        {
            var method = TypedGet(
                    (type, type2, fx.Method),
                    (k) => k.method
                        .GetGenericMethodDefinition()
                        .MakeGenericMethod(k.type1, k.type2)
                        .CreateTypedDelegate<Func<Target, T>>());
            return method(target);
        }

        public static T InvokeAs<Target, T1, T>(this Target target, Type type, Type type2, Func<T1, T> fx, T1 p1)
        {
            var method = TypedGet(
                    (type, type2, fx.Method),
                    (k) => k.method
                        .GetGenericMethodDefinition()
                        .MakeGenericMethod(k.type1, k.type2)
                        .CreateTypedDelegate<Func<Target, T1, T>>());
            return method(target, p1);
        }

        public static T InvokeAs<Target, T1, T2, T>(this Target target, Type type, Type type2, Func<T1, T2, T> fx, T1 p1, T2 p2)
        {
            var method = TypedGet(
                    (type, type2, fx.Method),
                    (k) => k.method
                        .GetGenericMethodDefinition()
                        .MakeGenericMethod(k.type1, k.type2)
                        .CreateTypedDelegate<Func<Target, T1, T2, T>>());
            return method(target, p1, p2);
        }
        public static T InvokeAs<Target, T1, T2, T3, T>(this Target target, Type type, Type type2, Func<T1, T2, T3, T> fx, T1 p1, T2 p2, T3 p3)
        {
            var method = TypedGet(
                    (type, type2, fx.Method),
                    (k) => k.method
                        .GetGenericMethodDefinition()
                        .MakeGenericMethod(k.type1, k.type2)
                        .CreateTypedDelegate<Func<Target, T1, T2, T3, T>>());
            return method(target, p1, p2, p3);
        }
    }

    //public static class GenericHelper1
    //{

    //    class GenericCache<T, RT>
    //        where T: notnull
    //    {

    //        internal static readonly ConcurrentDictionary<T, RT> cache
    //            = new ConcurrentDictionary<T, RT>();
    //    }

    //    private static T GetOrAdd<TKey, T>(TKey key, Func<TKey, T> factory)
    //        where TKey: notnull
    //    {
    //        return GenericCache<TKey, T>.cache.GetOrAdd(key, factory);
    //    }

    //    public class GenericMethod<T>
    //    {
    //        private MethodInfo method;
    //        private object? @delegate;

    //        public GenericMethod(MethodInfo method)
    //        {
    //            this.method = method;
    //            @delegate = null;
    //        }

    //        internal TDelegate CreateDelegate<TDelegate>()
    //        {
    //            return (TDelegate)(@delegate ??= method.CreateDelegate(typeof(TDelegate)));
    //        }
    //    }

    //    public struct GenericMethodWithTarget<T>
    //    {
    //        private readonly GenericMethod<T> method;
    //        private readonly T target;

    //        public GenericMethodWithTarget(GenericMethod<T> method, T target)
    //        {
    //            this.method = method;
    //            this.target = target;
    //        }

    //        public GenericMethod<T, RT> As<RT>() => new GenericMethod<T, RT>(method, target);


    //    }

    //    public struct GenericMethod<T, RT>
    //    {
    //        private GenericMethod<T> genericMethod;
    //        private readonly T target;

    //        public GenericMethod(GenericMethod<T> genericMethod, T target)
    //        {
    //            this.genericMethod = genericMethod;
    //            this.target = target;
    //        }

    //        public RT Invoke()
    //        {
    //            return genericMethod.CreateDelegate<Func<T, RT>>()(target);
    //        }

    //        public RT Invoke<T1>(T1 p1)
    //        {
    //            return genericMethod.CreateDelegate<Func<T, T1, RT>>()(target, p1);
    //        }

    //        public RT Invoke<T1, T2>(T1 p1, T2 p2)
    //        {
    //            return genericMethod.CreateDelegate<Func<T, T1, T2, RT>>()(target, p1, p2);
    //        }

    //        public RT Invoke<T1, T2, T3>(T1 p1, T2 p2, T3 p3)
    //        {
    //            return genericMethod.CreateDelegate<Func<T, T1, T2, T3, RT>>()(target, p1, p2, p3);
    //        }

    //        public RT Invoke<T1, T2, T3, T4>(T1 p1, T2 p2, T3 p3, T4 p4)
    //        {
    //            return genericMethod.CreateDelegate<Func<T, T1, T2, T3, T4, RT>>()(target, p1, p2, p3, p4);
    //        }

    //        public RT Invoke<T1, T2, T3, T4, T5>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5)
    //        {
    //            return genericMethod.CreateDelegate<Func<T, T1, T2, T3, T4, T5, RT>>()(target, p1, p2, p3, p4, p5);
    //        }

    //        public RT Invoke<T1, T2, T3, T4, T5, T6>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6)
    //        {
    //            return genericMethod.CreateDelegate<Func<T, T1, T2, T3, T4, T5, T6, RT>>()(target, p1, p2, p3, p4, p5, p6);
    //        }

    //    }

    //    public static GenericMethodWithTarget<T> GetInstanceGenericMethod<T>(this T target, string methodName, Type type)
    //        where T: notnull
    //    {
    //        var m = GetOrAdd((target, methodName, type), (x) =>
    //            {
    //                var method = typeof(T)
    //                    .GetMethod(methodName)!
    //                    .MakeGenericMethod(type);
    //                return new GenericMethod<T>(method);
    //            });
    //        return new GenericMethodWithTarget<T>(m, target);
    //    }

    //    public static GenericMethodWithTarget<T> GetInstanceGenericMethod<T>(this T target, 
    //        string methodName, 
    //        Type type1,
    //        Type type2)
    //        where T : notnull
    //    {
    //        var m = GetOrAdd((target, methodName, type1, type2), (x) =>
    //        {
    //            var method = typeof(T)
    //                .GetMethod(methodName)!
    //                .MakeGenericMethod(type1, type2);
    //            return new GenericMethod<T>(method);
    //        });
    //        return new GenericMethodWithTarget<T>(m, target);
    //    }

    //    public static GenericMethodWithTarget<T> GetInstanceGenericMethod<T>(this T target,
    //        string methodName,
    //        Type type1,
    //        Type type2,
    //        Type type3)
    //        where T : notnull
    //    {
    //        var m = GetOrAdd((target, methodName, type1, type2, type3), (x) =>
    //        {
    //            var method = typeof(T)
    //                .GetMethod(methodName)!
    //                .MakeGenericMethod(type1, type2, type3);
    //            return new GenericMethod<T>(method);
    //        });
    //        return new GenericMethodWithTarget<T>(m, target);
    //    }
    //}

    public static class TaskExtensions
    {
        public static async Task<object?> ContinueAsObject<T>(this Task<T> task)
        {
            var r = await task;
            return r;
        }

        public static async Task<object?> GetResultAsObject(this Task task)
        {
            var t = task.GetType();
            if (t.IsConstructedGenericType)
            {
                return await (Task<object?>)Generic.InvokeAs(t.GetFirstGenericArgument(), GetResultFrom<object>, task);
            }
            await task;
            return null;
        }

        private static async Task<object?> GetResultFrom<T>(Task task)
        {
            return await (task as Task<T>)!;
        }
    }
}
