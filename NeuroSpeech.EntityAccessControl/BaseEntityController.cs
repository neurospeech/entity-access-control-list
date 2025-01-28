using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using NeuroSpeech;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NeuroSpeech.EntityAccessControl.Internal;
using System.Threading;
using System.Text.Json.Nodes;
using NeuroSpeech.EntityAccessControl.Parser;
using Microsoft.AspNetCore.Http;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Linq.Expressions;
using NeuroSpeech.EntityAccessControl.Extensions;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace NeuroSpeech.EntityAccessControl
{

    public class IgnoreAuthorizationFilter : IAsyncAuthorizationFilter
    {
        public Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            return Task.CompletedTask;
        }
    }

    public class IgnoreAuthorization : TypeFilterAttribute
    {
        public IgnoreAuthorization() : base(typeof(IgnoreAuthorizationFilter))
        {
        }
    }

    [IgnoreAuthorization]
    public abstract class BaseEntityController : BaseController
    {

        public BaseEntityController(ISecureQueryProvider db) : base(db)
        {
            names ??= CreateNameMap();
        }

        private Dictionary<string, string>? CreateNameMap()
        {
            var d = new Dictionary<string, string>();
            foreach(var m in db.Model.GetEntityTypes())
            {
                foreach(var p in m.GetProperties())
                {
                    d["." + p.GetJsonPropertyName().ToLower()] = "." + (p.PropertyInfo?.Name ?? p.Name);
                }
                foreach(var np  in m.GetNavigations()) {
                    d["." + np.GetJsonPropertyName().ToLower()] = "." + (np.PropertyInfo?.Name ?? np.Name);
                }
            }
            return d;
        }

        [HttpGet("model")]
        public IActionResult Entities()
        {
            var naming = JsonNamingPolicy.CamelCase;
            return Ok(db.Model.GetEntityTypes().Select(x =>
            {
                var ignoreConditions = db.GetIgnoredProperties(x.ClrType);
                bool IsIgnored(IProperty property)
                {
                    return ignoreConditions.Contains(property.PropertyInfo);
                }
                return new
                {
                    x.Name,
                    Keys = x.GetProperties().Where(x => x.IsKey()).Select(p => new
                    {
                        Name = p.GetJsonPropertyName(),
                        Type = (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType).FullName,
                        Identity = p.GetValueGenerationStrategy() == SqlServerValueGenerationStrategy.IdentityColumn
                    }),
                    Properties = x.GetProperties().Where(x => !x.IsKey() && !IsIgnored(x)).Select(p => new
                    {
                        Name = p.GetJsonPropertyName(),
                        Type = (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType).FullName,
                        p.IsNullable
                    }),
                    NavigationProperties = x.GetNavigations().Select(np => new
                    {
                        Name = naming.ConvertName(np.Name),
                        np.IsCollection,
                        Type = np.TargetEntityType.ClrType.FullName
                    })
                };
            }));
        }

        [HttpGet("model.ts")]
        public IActionResult ModelDeclaration()
        {
            static string ModelName(string n)
            {
                return n.Split('.').Last();
            }

            var naming = JsonNamingPolicy.CamelCase;
            var sw = new System.IO.StringWriter();
            var i = new IndentedTextWriter(sw);

            i.WriteLine(@"import DateTime from ""@web-atoms/date-time/dist/DateTime"";
import type IClrEntity from ""@web-atoms/entity/dist/models/IClrEntity"";
import { ICollection, IGeometry, IModel, Model } from ""@web-atoms/entity/dist/services/BaseEntityService"";
");

            var enumTypes = new List<Type>();

            foreach (var e in db.Model.GetEntityTypes())
            {

                var name = ModelName(e.Name);
                var b = e.BaseType == null ? "ClrEntity" : ModelName(e.BaseType.Name);
                i.WriteLine($"export interface I{name} extends I{b} {{");
                i.Indent++;

                var ignoreConditions = db.GetIgnoredProperties(e.ClrType);

                foreach (var p in e.GetDeclaredProperties())
                {

                    if (ignoreConditions.Contains(p.PropertyInfo))
                    {
                        continue;
                    }

                    var clrType = Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType;
                    if (clrType.IsEnum) {
                        if (!enumTypes.Contains(clrType))
                            enumTypes.Add(clrType);
                        var typeName = $"IEnum{clrType.Name}";
                        if (p.IsNullable)
                        {
                            typeName += " | null";
                        }
                        i.WriteLine($"{p.GetJsonPropertyName()}?: {typeName};");
                        continue;
                    }
                    var type = p.ClrType.ToTypeScript();
                    if (p.IsNullable)
                    {
                        type += " | null";
                    }
                    i.WriteLine($"{p.GetJsonPropertyName()}?: {type};");
                }

                foreach(var p in e.ClrType.GetProperties().Where(x => x.GetCustomAttribute<ModelPropertyAttribute>() != null))
                {
                    var clrType = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                    var isNullable = clrType != p.PropertyType;
                    if (clrType.IsEnum)
                    {
                        if (!enumTypes.Contains(clrType))
                            enumTypes.Add(clrType);
                        var typeName = $"IEnum{clrType.Name}";
                        if (isNullable)
                        {
                            typeName += " | null";
                        }
                        i.WriteLine($"{p.GetJsonPropertyName()}?: {typeName};");
                        continue;
                    }
                    var type = p.PropertyType.ToTypeScript();
                    if (isNullable)
                    {
                        type += " | null";
                    }
                    i.WriteLine($"{p.GetJsonPropertyName()}?: {type};");
                }

                foreach (var np in e.GetDeclaredNavigations())
                {
                    var npName = $"I{ModelName(np.TargetEntityType.Name)}";
                    if (np.IsCollection)
                    {
                        i.WriteLine($"{naming.ConvertName(np.Name)}?: ICollection<{npName}>;");
                        continue;
                    }
                    i.WriteLine($"{naming.ConvertName(np.Name)}?: {npName};");
                }
                i.Indent--;
                i.WriteLine("}");
                i.WriteLine();

                if (e.IsOwned())
                    continue;
                i.WriteLine($"export const {name}: IModel<I{name}> = new Model<I{name}>(\"{e.Name}\");");
            }

            foreach (var enumType in enumTypes)
            {
                var names = string.Join(" | ", enumType.GetEnumNames().Select(x => $"\"{x}\""));
                i.WriteLine($"export type IEnum{enumType.Name} = {names};");
            }
            i.WriteLine();

            return Content(sw.ToString(), "text/plain");
        }

        [HttpPut]
        [HttpPost]
        public async Task<IActionResult> Save(
            [FromBody] JsonElement body,
            CancellationToken cancellationToken = default
            )
        {
            try
            {
                if (body.ValueKind == JsonValueKind.Array)
                    return await SaveMultiple(body, cancellationToken);
                var e = await LoadOrCreateAsync(null, body, cancellationToken: cancellationToken);
                await db.SaveChangesAsync();
                return Serialize(e);
            }catch (EntityAccessException eae)
            {
                if (eae.StackTrace!= null)
                    eae.ErrorModel.Add("Stack", eae.StackTrace);
                return this.UnprocessableEntity(eae.ErrorModel);
            }
        }

        [HttpPost("invoke/{entityName}/{methodName}")]
        public async Task<IActionResult> Invoke(
            [FromRoute] string entityName,
            [FromRoute] string methodName,
            [FromBody] JsonElement body,
            CancellationToken cancellationToken
            )
        {
            var et = FindEntityType(entityName);
            var iet = db.GetEntityEvents(et.ClrType);

            var entity = body.GetProperty("entity");
            var args = body.GetProperty("args");

            var e1 = await db.FindByKeysAsync(et, body, cancellationToken);

            if (e1 == null)
            {
                throw new EntityAccessException("Entity not found");
            }

            var r = await DbEntityMethodInvoker.CallMethod(db, methodName, e1, args);

            if (r == null)
            {
                return Json(null);
            }

            if (r is IActionResult ar)
            {
                return ar;
            }

            return Serialize(r);
        }


        [HttpPut("multiple")]
        [HttpPost("multiple")]
        public async Task<IActionResult> SaveMultiple(
            [FromBody] JsonElement model,
            CancellationToken cancellationToken
            )
        {
            try
            {
                List<object> results = new();
                foreach (var body in model.EnumerateArray())
                {
                    var result = await LoadOrCreateAsync(null, body, cancellationToken: cancellationToken);
                    results.Add(result);
                }
                await db.SaveChangesAsync();
                return Serialize(results);
            }catch (EntityAccessException ex)
            {
                if (ex.StackTrace != null)
                    ex.ErrorModel.Add("Stack", ex.StackTrace);
                return this.UnprocessableEntity(ex.ErrorModel);
            }
        }

        [HttpDelete]
        public async Task<IActionResult> Delete(
            [FromBody] JsonElement entity
            )
        {
            try
            {
                var t = FindEntityType(entity);
                var d = await db.FindByKeysAsync(t, entity);
                if (d != null)
                {
                    db.Remove(d);
                }
                await db.SaveChangesAsync();
                return Ok(new { });
            } catch( EntityAccessException ex)
            {
                if (ex.StackTrace != null)
                    ex.ErrorModel.Add("Stack", ex.StackTrace);
                return this.UnprocessableEntity(ex.ErrorModel);
            }
        }

        [HttpDelete("bulk")]
        public async Task<IActionResult> BulkDelete(
            [FromBody] BulkOperation model,
            CancellationToken cancellation
            )
        {
            if (model.Keys == null)
                return BadRequest();
            try
            {
                foreach (var item in model.Keys)
                {
                    var t = FindEntityType(item);
                    var entity = await db.FindByKeysAsync(t, item, cancellation);
                    if (entity == null)
                    {
                        if (model.ThrowWhenNotFound)
                        {
                            return BadRequest();
                        }
                        continue;
                    }
                    db.Remove(entity);
                }

                await db.SaveChangesAsync();
                return Ok(new { });
            }catch (EntityAccessException ex)
            {
                if (ex.StackTrace != null)
                    ex.ErrorModel.Add("Stack", ex.StackTrace);
                return UnprocessableEntity(ex.ErrorModel);
            }
        }

        public class BulkOperation
        {
            public JsonElement[]? Keys { get; set; }
            public JsonElement Update { get; set; }

            public bool ThrowWhenNotFound { get; set; }
        }

        [HttpPut("bulk")]
        public async Task<IActionResult> BulkSave(
            [FromBody] BulkOperation model,
            CancellationToken cancellation
            )
        {
            if (model.Keys == null
                || model.Update.ValueKind == JsonValueKind.Undefined
                || model.Update.ValueKind == JsonValueKind.Null)
                return BadRequest();

            try
            {
                foreach (var item in model.Keys)
                {
                    var t = FindEntityType(item);
                    var entity = await db.FindByKeysAsync(t, item, cancellation);
                    if (entity == null)
                    {
                        if (model.ThrowWhenNotFound)
                        {
                            return BadRequest();
                        }
                        continue;
                    }
                    await LoadPropertiesAsync(entity, t, model.Update);
                }
                await db.SaveChangesAsync();
                return Ok(new { });
            }catch (EntityAccessException eae)
            {
                if (eae.StackTrace != null)
                    eae.ErrorModel.Add("Stack", eae.StackTrace);
                return UnprocessableEntity(eae.ErrorModel);
            }
        }

        public class MethodOptions
        {
            public JsonElement Methods { get; set; }

            public string? Function { get; set; }

            public JsonElement Args { get; set; }

            public int Start { get; set; } = 0;
            public int Size { get; set; } = 200;
            public bool SplitInclude { get; set; } = true;

            public bool Split { get => SplitInclude; set => SplitInclude = value; }
            public bool Trace { get; set; }

            public int CacheSeconds { get; set; }
            public int Cache { get => CacheSeconds; set => CacheSeconds = value; }
            public bool? Count { get; set; }
        }

        [HttpGet("query/{entity}")]
        public Task<IActionResult> Query(
            [FromRoute] string entity,
            [FromQuery] string methods = "[]",
            [FromQuery] int start = 0,
            [FromQuery] int size = 200,
            [FromQuery] bool split = true,
            [FromQuery] bool trace = false,
            [FromQuery] int cache = 0,
            [FromQuery] bool count = true,
            [FromQuery] string? function = null,
            [FromQuery] string args = "[]",
            CancellationToken cancellationToken = default
        )
        {
            return PostMethod(entity, new MethodOptions
            {
                Methods = JsonDocument.Parse(methods ?? "[]").RootElement,
                Start = start,
                Size = size,
                Count = count,
                SplitInclude = split,
                Trace = trace,
                Cache = cache,
                Function = function,
                Args = JsonDocument.Parse(args ?? "[]").RootElement,
            }, cancellationToken);
        }

//        [HttpGet("c2/{entity}/{json}")]
//        public Task<IActionResult> CachedMethodsV2(
//            [FromRoute] string entity,
//            [FromRoute] string json,
//            CancellationToken cancellationToken = default
//)
//        {
//            return PostMethod(entity, System.Text.Json.JsonSerializer.Deserialize<MethodOptions>(json)!, cancellationToken);
//        }

        [HttpGet("methods/{entity}")]
        public Task<IActionResult> Methods(
            [FromRoute] string entity,
            [FromQuery] string methods,
            [FromQuery] int start = 0,
            [FromQuery] int size = 200,
            [FromQuery] bool splitInclude = true,
            [FromQuery] bool trace = false,
            [FromQuery] int cacheSeconds = 0,
            [FromQuery] bool count = true,
            CancellationToken cancellationToken = default
            )
        {
            return PostMethod(entity, new MethodOptions { 
                Methods = JsonDocument.Parse(methods).RootElement,
                Start = start,
                Size = size,
                Count = count,
                SplitInclude = splitInclude,
                Trace = trace,
                CacheSeconds = cacheSeconds
            }, cancellationToken);
        }

        [HttpGet("cached/{entity}/{methods}/{start}/{size}/{splitInclude}/{trace}/{cacheSeconds}")]
        public Task<IActionResult> CachedMethods(
            [FromRoute] string entity,
            [FromRoute] string methods,
            [FromRoute] int start = 0,
            [FromRoute] int size = 200,
            [FromRoute] bool splitInclude = true,
            [FromRoute] bool trace = false,
            [FromRoute] int cacheSeconds = 0,
            [FromRoute] bool count = true,
            CancellationToken cancellationToken = default
        )
        {
            return PostMethod(entity, new MethodOptions
            {
                Methods = JsonDocument.Parse(methods).RootElement,
                Start = start,
                Size = size,
                SplitInclude = splitInclude,
                Count = count,
                Trace = trace,
                CacheSeconds = cacheSeconds
            }, cancellationToken);
        }

        protected virtual void TraceQuery(string text)
        {
            System.Diagnostics.Debug.WriteLine(text);
        }


        [HttpPost("methods/{entity}")]
        public async Task<IActionResult> PostMethod(
            [FromRoute] string entity,
            [FromBody] MethodOptions model,
            CancellationToken cancellationToken = default
            )
        {
            var methods = model.Methods;
            var t = FindEntityType(entity);
            var hasSelect = false;
            var hasInclude = false;
            var options = new LinqMethodOptions();
            List<LinqMethod> methodList = new();
            options.Methods = methodList;
            options.Start = model.Start;
            options.Size = model.Size;
            options.Function = model.Function;
            options.Parameters = model.Args;
            options.SplitInclude = model.SplitInclude;
            options.CancelToken = cancellationToken;
            // default is true if not supplied...
            string? typeName;
            string? left;
            string? right;
            options.Count = model.Count ?? true;
            if (model.Trace)
            {
                options.Trace = TraceQuery;
            }

            var root = methods;
            if (root.ValueKind == JsonValueKind.String)
            {
                root = JsonDocument.Parse(root.AsString()!).RootElement;
            }
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var method in root.EnumerateArray())
                {
                    LinqMethod lm = new();

                    if (method.ValueKind != JsonValueKind.Array)
                    {
                        throw new NotSupportedException($"Each method should be an array with format [name, query, ... parameters ]");
                    }

                    int n = method.GetArrayLength();
                    if (n < 2)
                    {
                        throw new ArgumentException($"Method Siganture must have two elements atleast");
                    }

                    string name = method[0].GetString()!;

                    lm.Expression = method[1].GetString()!;

                    for (int i = 2; i < n; i++)
                    {
                        lm.Parameters.Add(new QueryParameter(method[i]));
                    }

                    switch (name)
                    {
                        case "select":
                            lm.Method = "Select";
                            hasSelect = true;
                            break;

                        case "groupBy":
                            lm.Method = "GroupBy";
                            break;
                        case "where":
                            lm.Method = "Where";
                            break;
                        case "orderBy":
                            lm.Method = "OrderBy";
                            break;
                        case "orderByDescending":
                            lm.Method = "OrderByDescending";
                            break;
                        case "thenBy":
                            lm.Method = "ThenBy";
                            break;
                        case "thenByDescending":
                            lm.Method = "ThenByDescending";
                            break;
                        case "dateRange":
                            lm.Method = "DateRange";
                            lm.Expression = "@0, @1, @2";
                            break;
                        case "joinDateRange":
                            lm.Method = "JoinDateRange";
                            lm.Expression = "@0, @1, @2";
                            break;

                        case "join":
                            typeName = method[1].GetString();
                            left = method[2].GetString();
                            right = method[3].GetString();

                            // this will ensure that the type exits..
                            FindEntityType(typeName);

                            lm.Method = $"Container().JoinWith<{typeName}>().Join({left}, {right})";
                            lm.Expression = null;
                            break;

                        case "leftJoin":
                            typeName = method[1].GetString();
                            left = method[2].GetString();
                            right = method[3].GetString();

                            // this will ensure that the type exits..
                            FindEntityType(typeName);

                            lm.Method = $"Container().JoinWith<{typeName}>().LeftJoin({left}, {right})";
                            lm.Expression = null;
                            break;

                        case "selectWith":
                            typeName = method[1].GetString();

                            // this will ensure that the type exits..
                            FindEntityType(typeName);

                            lm.Method = $"Container().JoinWith<{typeName}>()";
                            lm.Expression = null;
                            lm.Parameters.Clear();
                            break;
                        case "include":
                            lm.Method = "IncludeSecure";
                            hasInclude = true;
                            if (!lm.Expression.Contains("=>"))
                                lm.Expression = System.Text.Json.JsonSerializer.Serialize(lm.Expression);
                            break;
                        case "thenInclude":
                            lm.Method = "ThenIncludeSecure";
                            if (!lm.Expression.Contains("=>"))
                                lm.Expression = System.Text.Json.JsonSerializer.Serialize(lm.Expression);
                            break;
                        default:
                            continue;
                    }

                    methodList.Add(lm);
                }
            }
            if (options.SplitInclude)
            {
                options.SplitInclude = hasInclude && !hasSelect;
            }
            var result = await this.InvokeAs(t.ClrType, InvokeAsync<object>, options);
            var cacheSeconds = model.CacheSeconds;
            if (cacheSeconds > 0)
            {
                SetupCacheHeaders(cacheSeconds);
            }
            return result;
        }

        protected virtual void SetupCacheHeaders(int cacheSeconds)
        {
            var headers = Response.GetTypedHeaders();
            headers.CacheControl = new Microsoft.Net.Http.Headers.CacheControlHeaderValue
            {
                MaxAge = TimeSpan.FromSeconds(cacheSeconds),
                Public = true,
            };
        }

        public async Task<IActionResult> InvokeAsync<T>(
            LinqMethodOptions options)
            where T : class
        {
            var q = options.Function != null
                ? await DbFunctionInvoker.CallFunction<T>(db, options.Function, options.Parameters)
                : (db.EnforceSecurity ? db.FilteredQuery<T>() : db.Set<T>());
            var type = db.GetType();
            options.Type = type;
            options.Names = names!;
            var result = await MethodParser.Instance.Parse<T>(q, options);
            return Serialize(result);
        }

        private static Dictionary<string, string>? names;


        //[HttpGet("query/{entity}")]
        //public async Task<IActionResult> Query(
        //    [FromRoute] string entity,
        //    [FromQuery] string? select = null,
        //    [FromQuery] string? selectParameters = null,
        //    [FromQuery] string? filter = null,
        //    [FromQuery] string? parameters = null,
        //    [FromQuery] string? keys = null,
        //    [FromQuery] string? include = null,
        //    [FromQuery] string? orderBy = null,
        //    [FromQuery] bool splitInclude = true,
        //    [FromQuery] int start = 0,
        //    [FromQuery] int size = 200
        //    )
        //{

        //    var t = FindEntityType(entity);

        //    //this.GetInstanceGenericMethod(nameof(ListAsync), t.ClrType)
        //    //    .As<Task<IActionResult>>().Invoke()

        //    var r = this.GetType()
        //        .GetMethod(nameof(ListAsync))!
        //        .MakeGenericMethod(t.ClrType)
        //        .Invoke(this, new object?[] {
        //            t,
        //            select,
        //            selectParameters,
        //            filter,
        //            parameters,
        //            keys,
        //            include,
        //            orderBy,
        //            splitInclude,
        //            start,
        //            size }) as Task<IActionResult>;
        //    return await r!;
        //}

        //private static readonly ParsingConfig config = new()
        //{
        //    ResolveTypesBySimpleName = true
        //};


        //public async Task<IActionResult> ListAsync<T>(
        //    IEntityType entityType,
        //    string? select,
        //    string? selectParameters,
        //    string? filter, 
        //    string? parameters, 
        //    string? keys,
        //    string? include,
        //    string? orderBy, 
        //    bool splitInclude,
        //    int start, int size)
        //    where T : class
        //{
        //    var q = db.Query<T>();

        //    var includeList = ParseInclude(include);
        //    List<string>? ParseInclude(string? tx)
        //    {
        //        tx = tx?.Trim('[', ' ', '\t', '\r', '\n', ']');
        //        if (string.IsNullOrWhiteSpace(tx))
        //            return null;
        //        var includeKeys = new List<string>();
        //        foreach (var token in tx.Split(',', ';'))
        //        {
        //            var x = token.Trim('"',' ', '\r', '\t' , '\n');
        //            var key = "";
        //            var scope = entityType;
        //            while (x.Length > 0)
        //            {
        //                int index = x.IndexOf('.');
        //                if (index == -1)
        //                {
        //                    if (!scope.GetNavigations().TryGetFirst(n => n.Name.EqualsIgnoreCase(x), out var np))
        //                        throw new KeyNotFoundException($"No navigation property {x} found in {scope.Name}");
        //                    key += np.Name;
        //                    includeKeys.Add(key);
        //                    break;
        //                }
        //                var left = x[..index];
        //                x = x[(index + 1)..];
        //                if (!scope.GetNavigations().TryGetFirst(n => n.Name.EqualsIgnoreCase(left), out var leftProperty))
        //                    throw new KeyNotFoundException($"No navigation property {x} found in {scope.Name}");
        //                scope = leftProperty.TargetEntityType;
        //                key += leftProperty.Name + ".";
        //            }
        //        }
        //        return includeKeys;
        //    }

        //    if (keys != null)
        //    {
        //        var type = typeof(T);
        //        var pe = Expression.Parameter(type);
        //        Expression? body = null;
        //        foreach (var key in JsonSerializer.Deserialize<JsonElement>(keys).EnumerateObject())
        //        {
        //            if(!type.GetProperties().TryGetFirst(x => x.Name.EqualsIgnoreCase(key.Name), out var property))
        //                throw new KeyNotFoundException($"No navigation property {key.Name} found in {type.Name}");
        //            var compare = Expression.Equal(
        //                    Expression.Property(pe, property),
        //                    Expression.Constant(key.Value.DeserializeJsonElement(property.PropertyType)));
        //            body = body == null ? compare : Expression.AndAlso(body, compare);
        //        }
        //        q = q.Where(Expression.Lambda(body, pe));
        //    }
        //    if (!string.IsNullOrWhiteSpace(filter))
        //    {
        //        if (filter.StartsWith("["))
        //        {
        //            var filters = JsonSerializer.Deserialize<string[]>(filter);
        //            var parameterArray = JsonDocument.Parse(parameters ?? "[]").RootElement;
        //            for (int i = 0; i < filters.Length; i++)
        //            {
        //                filter = filters[i];
        //                q = await q.WhereLinqAsync(filter, parameterArray[i]);
        //            }
        //        }
        //        else
        //        {
        //            q = await q.WhereLinqAsync(filter, JsonDocument.Parse(parameters ?? "[]").RootElement);
        //        }
        //    }
        //    if (!string.IsNullOrWhiteSpace(orderBy))
        //    {
        //        q = q.OrderBy(orderBy);
        //    }
        //    var original = q;
        //    bool pageResult = false;
        //    if (start > 0)
        //    {
        //        q = q.Skip(start);
        //        pageResult = true;
        //    }
        //    if (size > 0)
        //    {
        //        q = q.Take(size);
        //        pageResult = true;
        //    }

        //    var total = 0;

        //    if (pageResult)
        //    {
        //        total = await original.CountAsync();

        //    }

        //    if (includeList != null)
        //    {
        //        foreach(var i in includeList)
        //        {
        //            q = q.Include(i);
        //        }

        //        if (splitInclude)
        //        {
        //            q = q.AsSplitQuery();
        //        }
        //    }

        //    var json = new JsonArray();
        //    if (!string.IsNullOrWhiteSpace(select))
        //    {
        //        var qc = new QueryContext<T>(db, q);
        //        var dl = await qc.SelectLinqAsync(select, JsonDocument.Parse(selectParameters ?? "[]").RootElement);
        //        foreach(var item in dl)
        //        {
        //            json.Add(item);
        //        }
        //    }
        //    else
        //    {
        //        var list = await q.ToListAsync(this.HttpContext?.RequestAborted ?? default);
        //        json = SerializeList(list);
        //    }

        //    return Json(new
        //    {
        //        items = json,
        //        total
        //    });
        //}
    }
}

