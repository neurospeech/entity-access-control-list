﻿using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NeuroSpeech.EntityAccessControl
{
    public class EntityJsonConverterFactory : JsonConverterFactory
    {
        private readonly EntitySerializationSettings settings;

        public EntityJsonConverterFactory(EntitySerializationSettings settings)
        {
            this.settings = settings;
        }

        public override bool CanConvert(Type typeToConvert)
        {
            if (typeToConvert.IsValueType)
                return false;
            if (typeToConvert == typeof(string))
                return false;
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(typeToConvert))
                return false;
            return true;
        }

        public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            return new EntityJsonConverter(settings);
        }

        class EntityJsonConverter : JsonConverter<object>
        {

            static readonly string DateFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'FFFFFFFZ";

            private EntitySerializationSettings settings;
            private readonly Dictionary<object, string> added = new(ReferenceEqualityComparer.Instance);

            public EntityJsonConverter(EntitySerializationSettings settings)
            {
                this.settings = settings;
            }

            public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotImplementedException();
            }

            public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
            {
                if (value == null)
                {
                    writer.WriteNullValue();
                    return;
                }

                if (value is string sv)
                {
                    writer.WriteStringValue(sv);
                    return;
                }

                if (value is System.Collections.IEnumerable enumerable)
                {
                    writer.WriteStartArray();
                    foreach (var item in enumerable)
                    {
                        if (item == null)
                        {
                            writer.WriteNullValue();
                            continue;
                        }
                        Write(writer, item, options);
                    }
                    writer.WriteEndArray();
                    return;
                }

                if (added.TryGetValue(value, out var existingIndex))
                {
                    writer.WriteStartObject();
                    writer.WriteString("$id", existingIndex);
                    writer.WriteEndObject();
                    return;
                }
                var index = added.Count;
                existingIndex = index.ToString();
                added[value] = existingIndex;
                writer.WriteStartObject();

                // write id
                writer.WriteString("$id", existingIndex);

                var et = value.GetType();
                var d = et.StaticCacheGetOrCreate((et) => settings.GetTypeName?.Invoke(et) ?? et.FullName);
                var namingPolicy = settings.NamingPolicy ?? JsonNamingPolicy.CamelCase;

                writer.WriteString("$type", d);

                var properties = et.StaticCacheGetOrCreate((et) =>
                    et.GetProperties()
                    .Where(p =>
                        !(p.GetIndexParameters()?.Length > 0))
                    .Select(p =>
                    {
                        var propertyType = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                        return new
                        {
                            PropertyType = propertyType,
                            p.Name,
                            GetValue = (Func<object, object>)p.GetValue!,
                            PropertyInfo = p,
                            TypeCode = Type.GetTypeCode(propertyType)
                        };
                    })
                    .ToList());

                foreach (var p in properties)
                {
                    var ignoreCondition = settings.GetIgnoreCondition(p.PropertyInfo);

                    if (ignoreCondition == JsonIgnoreCondition.Always)
                        continue;
                    var name = namingPolicy.ConvertName(p.Name);
                    var propertyType = p.PropertyType;
                    var v = p.GetValue(value);
                    if (v == null)
                    {
                        if (ignoreCondition == JsonIgnoreCondition.WhenWritingNull)
                        {
                            continue;
                        }
                        // r[name] = null;
                        writer.WriteNull(name);
                        continue;
                    }
                    switch (p.TypeCode)
                    {
                        case TypeCode.Boolean:
                            // r[name] = JsonValue.Create((bool)v);
                            writer.WriteBoolean(name,(bool)v);
                            continue;
                        case TypeCode.Char:
                            writer.WriteString(name, v.ToString());
                            continue;
                        case TypeCode.SByte:
                            writer.WriteNumber(name, (sbyte)v);
                            continue;
                        case TypeCode.Byte:
                            writer.WriteNumber(name, (byte)v);
                            continue;
                        case TypeCode.Int16:
                            writer.WriteNumber(name, (Int16)v);
                            continue;
                        case TypeCode.UInt16:
                            writer.WriteNumber(name, (UInt16)v);
                            continue;
                        case TypeCode.Int32:
                            if (propertyType.IsEnum)
                            {
                                // r[name] = propertyType.GetEnumName(v)!;
                                writer.WriteString(name, propertyType.GetEnumName(v));
                                continue;
                            }
                            writer.WriteNumber(name, (UInt32)v);
                            continue;
                        case TypeCode.UInt32:
                            writer.WriteNumber(name, (UInt32)v);
                            continue;
                        case TypeCode.Int64:
                            writer.WriteNumber(name, (Int64)v);
                            continue;
                        case TypeCode.UInt64:
                            writer.WriteNumber(name, (UInt64)v);
                            continue;
                        case TypeCode.Single:
                            writer.WriteNumber(name, (Single)v);
                            continue;
                        case TypeCode.Double:
                            writer.WriteNumber(name, (Double)v);
                            continue;
                        case TypeCode.Decimal:
                            writer.WriteNumber(name, (decimal)v);
                            continue;
                        case TypeCode.DateTime:
                            // r[name] = JsonValue.Create(((DateTime)v).ToString(DateFormat));
                            writer.WriteString(name, ((DateTime)v).ToString(DateFormat));
                            continue;
                        case TypeCode.String:
                            // r[name] = JsonValue.Create((string)v);
                            writer.WriteString(name, (string)v);
                            continue;
                    }
                    if (propertyType == typeof(DateTimeOffset))
                    {
                        // r[name] = ((DateTimeOffset)v).UtcDateTime.ToString(DateFormat);
                        writer.WriteString(name, ((DateTimeOffset)v).UtcDateTime.ToString(DateFormat));
                        continue;
                    }
                    if (propertyType == typeof(Guid))
                    {
                        // r[name] = ((Guid)v).ToString();
                        writer.WriteString(name, ((Guid)v).ToString());
                        continue;
                    }
                    if (v is JsonNode jn)
                    {
                        // r[name] = jn;
                        writer.WritePropertyName(name);
                        writer.WriteRawValue(jn.ToString(), true);
                        continue;
                    }
                    if (v is Geometry g)
                    {
                        // r[name] = g.ToString();
                        writer.WriteString(name, g.ToString());
                        continue;
                    }
                    if (v is System.Collections.IDictionary vd)
                    {
                        writer.WritePropertyName(name);
                        writer.WriteStartObject();
                        var ve = vd.GetEnumerator();
                        while (ve.MoveNext())
                        {
                            if (ve.Key == null)
                                continue;
                            var keyName = namingPolicy.ConvertName(ve.Key.ToString()!);
                            if (ve.Value == null)
                            {
                                writer.WriteNull(keyName);
                                continue;
                            }
                            writer.WritePropertyName(keyName);
                            Write(writer, ve.Value, options);
                        }
                        writer.WriteEndObject();
                        continue;
                    }
                    if (v is System.Collections.IEnumerable coll)
                    {
                        writer.WritePropertyName(name);
                        writer.WriteStartArray();
                        foreach(var c in coll)
                        {
                            if (c == null)
                                continue;
                            Write(writer, c, options);
                        }
                        writer.WriteEndArray();
                        //var list = new JsonArray();
                        //r[name] = list;
                        //pending.Enqueue(() =>
                        //{
                        //    foreach (var c in coll)
                        //    {
                        //        if (c != null)
                        //        {
                        //            var jc = SerializeToJson(c);
                        //            list.Add(jc);
                        //        }
                        //    }
                        //});
                        continue;
                    }
                    //  r[name] = SerializeToJson(v);
                    writer.WritePropertyName(name);
                    Write(writer, v, options);
                }

                writer.WriteEndObject();
            }
        }
    }

}