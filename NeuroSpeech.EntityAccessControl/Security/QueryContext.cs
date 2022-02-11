﻿using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using NeuroSpeech.EntityAccessControl.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace NeuroSpeech.EntityAccessControl
{

    public readonly struct QueryContext<T>: IOrderedQueryContext<T>
        where T: class
    {
        internal readonly ISecureQueryProvider db;
        internal readonly IQueryable<T> queryable;
        internal readonly ErrorModel? errorModel;

        public QueryContext(ISecureQueryProvider db, IQueryable<T> queryable, ErrorModel? errorModel = null)
        {
            this.db = db;
            this.queryable = queryable;
            this.errorModel = errorModel;
        }

        public IQueryContext<T1> Set<T1>()
            where T1: class
        {
            return new QueryContext<T1>(db, db.Query<T1>(), errorModel);
        }

        public IQueryContext<T> Where(Expression<Func<T, bool>> filter)
        {
            return new QueryContext<T>(db, queryable.Where(filter), errorModel);
        }

        public IQueryContext<T> Requires(Expression<Func<T, bool>> filter, string error)
        {
            errorModel?.Add("Error", error);
            return new QueryContext<T>(db, queryable.Where(filter), errorModel);
        }

        public IQueryable<T> ToQuery()
        {
            return queryable;
        }

        public IQueryContext<T1> OfType<T1>()
            where T1: class
        {
            return new QueryContext<T1>(db, queryable.OfType<T1>(), errorModel);
        }

        public IQueryContext<T2> Select<T2>(Expression<Func<T, T2>> expression)
            where T2: class
        {
            var ne = expression.Body as NewExpression;
            var list = new List<Expression>();
            foreach(var p in ne.Arguments)
            {
                list.Add(Replace(p, true));
            }
            ne = ne.Update(list);
            expression = Expression.Lambda<Func<T, T2>>(ne, expression.Parameters);
            return new QueryContext<T2>(db, queryable.Select(expression), errorModel);
        }

        internal Expression<Func<TInput,TOutput>> Replace<TInput,TOutput>(Expression<Func<TInput,TOutput>> exp)
        {
            return exp.Update(Replace(exp.Body), exp.Parameters);
        }

        internal Expression<Func<TInput, IEnumerable<TOutput>>> ReplaceEnumerable<TInput, TOutput>(Expression<Func<TInput, IEnumerable<TOutput>>> exp)
        {
            return exp.Update(Replace(exp.Body, false, typeof(IEnumerable<TOutput>)), exp.Parameters);
        }

        internal Expression<Func<TInput, IEnumerable<TOutput>>> Replace<TInput, TOutput>(Expression<Func<TInput, IEnumerable<TOutput>>> exp)
        {
            return exp.Update(Replace(exp.Body, false, typeof(IEnumerable<TOutput>)), exp.Parameters);
        }


        Expression Replace(Expression original, bool toList = false, Type? returnType = null)
        {
            if (returnType == null)
            {
                returnType = original.Type;
            }
            if (original is MemberExpression memberExpressiion)
            {
                if (memberExpressiion.Expression is not ParameterExpression)
                {
                    var ne = Replace(memberExpressiion.Expression);
                    if (ne.NodeType == ExpressionType.Constant 
                        && ne is ConstantExpression ce 
                        && ce.Value == null)
                    {
                        return Expression.Constant(null, original.Type);
                    }
                    if (ne != memberExpressiion.Expression)
                    {
                        memberExpressiion = memberExpressiion.Update(ne);
                    }
                }

                var property = (memberExpressiion.Member as PropertyInfo)!;
                var igc = db.GetIgnoreCondition(property);
                if (igc == JsonIgnoreCondition.Always)
                {
                    return Expression.Constant(null, original.Type);
                }

                var nav = db.Model.FindEntityType(property.DeclaringType)
                    .GetNavigations()
                    .FirstOrDefault(x => x.PropertyInfo == property);
                if (nav?.IsCollection ?? false)
                {
                    var itemType = nav.TargetEntityType.ClrType;

                    // apply where...
                    return this.GetInstanceGenericMethod(nameof(Apply), itemType)
                        .As<Expression>()
                        .Invoke(memberExpressiion, toList, returnType);
                }
            }
            return original;
        }

        public Expression Apply<T1>(Expression expression, bool toList, Type returnType)
            where T1: class
        {
            var qec = new QueryExpressionContext<T1>(new QueryContext<T1>(db, db.Query<T1>()!, errorModel), expression);
            var r = db.Apply<T1>(qec);
            qec = (QueryExpressionContext<T1>)r;
            var fe = qec.Expression;
            if (!returnType.IsAssignableFrom(fe.Type))
            {
                if (toList)
                {
                    // tolist required...
                    return QueryExpressionContext<T1>.ToList(fe);
                }
                return Expression.TypeAs(fe, expression.Type);
            }
            return fe;
        }

        public IQueryContext<T> Skip(int n)
        {
            return new QueryContext<T>(db, queryable.Skip(n), errorModel);
        }

        public IQueryContext<T> Take(int n)
        {
            return new QueryContext<T>(db, queryable.Take(n), errorModel);
        }

        public Task<int> CountAsync(CancellationToken cancellationToken)
        {
            return queryable.CountAsync(cancellationToken);
        }

        public string ToQueryString()
        {
            return queryable.ToQueryString();
        }

        public Task<List<T>> ToListAsync(CancellationToken cancellationToken)
        {
            return queryable.ToListAsync(cancellationToken);
        }

        public IQueryContext<T> Include(string include)
        {
            var q = this.queryable;
            var type = typeof(T);
            int index = include.IndexOf('.');
            var propertyName = index == -1 ? include : include.Substring(0, index);
            var propertyInfo = type.GetPropertyIgnoreCase(propertyName);
            var propertyType = propertyInfo.PropertyType;
            bool isList = propertyType.TryGetEnumerableItem(out var itemPropertyType);
            if (isList)
            {
                propertyType = typeof(IEnumerable<>).MakeGenericType(itemPropertyType!);
            }
            q = this.GetInstanceGenericMethod(nameof(IncludeProperty), type, propertyType)
                .As<IQueryable<T>>()
                .Invoke(q, propertyInfo);
            while(index != -1)
            {
                var tp = propertyType;
                index = include.IndexOf('.');
                propertyName = index == -1 ? include : include.Substring(0, index);
                propertyInfo = type.GetPropertyIgnoreCase(propertyName);
                q = this.GetInstanceGenericMethod(nameof(ThenIncludeProperty), type, tp, propertyInfo.PropertyType)
                    .As<IQueryable<T>>()
                    .Invoke(q, propertyInfo);
            }
            return new QueryContext<T>(db, q, errorModel);
        }

        public IQueryable<TE> IncludeProperty<TE, TP>(IQueryable<TE> q,
            PropertyInfo propertyInfo)
            where TE: class
            where TP: class
        {
            var pe = Expression.Parameter(typeof(TE));
            var property = Replace(Expression.Property(pe, propertyInfo), false, typeof(TP));
                
            var lambda = Expression.Lambda<Func<TE,TP>>(property, pe);
            return q.Include(lambda);
        }

        public IQueryable<TE> ThenIncludeProperty<TE, TP, TProperty>(IIncludableQueryable<TE, TP> q,
            PropertyInfo propertyInfo)
            where TE : class
            where TP : class
        {
            var pe = Expression.Parameter(typeof(TP));
            var property = Replace(Expression.Property(pe, propertyInfo));

            var lambda = Expression.Lambda<Func<TP, TProperty>>(property, pe);
            return q.ThenInclude(lambda);
        }

        public IOrderedQueryContext<T> ThenBy(Expression<Func<T, object>> expression)
        {
            return new QueryContext<T>(db, (queryable as IOrderedQueryable<T>).ThenBy(expression), errorModel);
        }

        public IOrderedQueryContext<T> ThenByDescending(Expression<Func<T, object>> expression)
        {
            return new QueryContext<T>(db, (queryable as IOrderedQueryable<T>).ThenByDescending(expression), errorModel);
        }

        public IOrderedQueryContext<T> OrderBy<TP>(Expression<Func<T, TP>> expression)
        {
            return new QueryContext<T>(db, queryable.OrderBy(expression), errorModel);
        }

        public IOrderedQueryContext<T> OrderByDescending<TP>(Expression<Func<T, TP>> expression)
        {
            return new QueryContext<T>(db, queryable.OrderByDescending(expression), errorModel);
        }

        public IQueryContext<T> AsSplitQuery()
        {
            return new QueryContext<T>(db, queryable.AsSplitQuery(), errorModel);
        }

        public IIncludableQueryContext<T, TProperty> Include<TProperty>(Expression<Func<T, TProperty>> path)            
        {
            if (typeof(TProperty).TryGetEnumerableItem(out var itemType))
            {

                var r = this.GetInstanceGenericMethod(nameof(IncludeChildren), itemType, typeof(TProperty))
                    .As<IIncludableQueryContext<T,TProperty>>()
                    .Invoke(path.Body, path.Parameters);
                var rc = r as IIncludableQueryContext<T, TProperty>;
                return rc;
            }
            var q = queryable.Include(Replace(path));
            return new IncludableQueryContext<T, TProperty>(db, q, errorModel);
        }

        public IIncludableQueryContext<T,TEnumerableProperty> IncludeChildren<TProperty, TEnumerableProperty>(
            Expression body, 
            IReadOnlyCollection<ParameterExpression> parameters)
            where TProperty : class
            where TEnumerableProperty: IEnumerable<TProperty>
        {
            var path = Expression.Lambda<Func<T, IEnumerable<TProperty>>>(body, parameters);
            var q = queryable.Include(ReplaceEnumerable<T, TProperty>(path));
            return new IncludableQueryContext<T, TEnumerableProperty>(db, q, errorModel);
        }

    }

    public readonly struct IncludableQueryContext<T, TP> : IIncludableQueryContext<T, TP>
        where T : class
    {
        private readonly QueryContext<T> qc;

        public IncludableQueryContext(ISecureQueryProvider db, IQueryable<T> queryable, ErrorModel? errorModel = null)
        {
            this.qc = new QueryContext<T>(db, queryable, errorModel);
        }

        public IIncludableQueryContext<T, TProperty> ThenInclude<TProperty>(Expression<Func<TP, TProperty>> path)
        {
            if (typeof(TProperty).TryGetEnumerableItem(out var itemType))
            {

                var r = this.GetInstanceGenericMethod(nameof(ThenIncludeChildren), itemType, typeof(TProperty))
                    .As<IIncludableQueryContext<T, TProperty>>()
                    .Invoke(path.Body, path.Parameters);
                var rc = r as IIncludableQueryContext<T, TProperty>;
                return rc;
            }
            var iq = (IIncludableQueryable<T, TP>)qc.queryable;
            var q = iq.ThenInclude(qc.Replace(path));
            return new IncludableQueryContext<T, TProperty>(qc.db, q, qc.errorModel);
        }

        public IIncludableQueryContext<T, TEnumerableProperty> ThenIncludeChildren<TProperty, TEnumerableProperty>(
            Expression body,
            IReadOnlyCollection<ParameterExpression> parameters)
            where TProperty : class
            where TEnumerableProperty : IEnumerable<TProperty>
        {
            var path = Expression.Lambda<Func<TP, IEnumerable<TProperty>>>(body, parameters);
            var iq = (IIncludableQueryable<T, TP>)qc.queryable;
            var q = iq.ThenInclude(qc.Replace(path));
            return new IncludableQueryContext<T, TEnumerableProperty>(qc.db, q, qc.errorModel);
        }

        public IIncludableQueryContext<T, TProperty> AsCollectionThenInclude<TPreviousProperty, TProperty>(Expression<Func<TPreviousProperty, TProperty>> path) where TPreviousProperty : class
        {
            if (typeof(TProperty).TryGetEnumerableItem(out var itemType))
            {

                var r = this.GetInstanceGenericMethod(nameof(AsCollectionThenIncludeChildren), typeof(TPreviousProperty), itemType, typeof(TProperty))
                    .As<IIncludableQueryContext<T, TProperty>>()
                    .Invoke(path.Body, path.Parameters);
                var rc = r as IIncludableQueryContext<T, TProperty>;
                return rc;
            }
            var iq = (IIncludableQueryable<T, IEnumerable<TPreviousProperty>>)qc.queryable;
            var q = iq.ThenInclude(qc.Replace(path));
            return new IncludableQueryContext<T, TProperty>(qc.db, q, qc.errorModel);
        }

        public IIncludableQueryContext<T, TEnumerableProperty> AsCollectionThenIncludeChildren<TPreviousProperty, TProperty, TEnumerableProperty>(
            Expression body,
            IReadOnlyCollection<ParameterExpression> parameters)
            where TProperty : class
            where TEnumerableProperty : IEnumerable<TProperty>
        {
            var path = Expression.Lambda<Func<TPreviousProperty, IEnumerable<TProperty>>>(body, parameters);
            var iq = (IIncludableQueryable<T, IEnumerable<TPreviousProperty>>)qc.queryable;
            var q = iq.ThenInclude(qc.ReplaceEnumerable<TPreviousProperty, TProperty>(path));
            return new IncludableQueryContext<T, TEnumerableProperty>(qc.db, q, qc.errorModel);
        }

        public IQueryContext<T1> OfType<T1>() where T1 : class
        {
            return qc.OfType<T1>();
        }
    }

    //public class IncludableChildrenQueryContext<T, ITP, TP> : QueryContext<T>
    //    , IIncludableQueryContext<T, ITP>
    //    where T : class
    //    where TP: class
    //    where ITP: IEnumerable<TP>
    //{
    //    public IncludableChildrenQueryContext(ISecureQueryProvider db, IQueryable<T> queryable, ErrorModel? errorModel = null)
    //        : base(db, queryable, errorModel)
    //    {
    //    }

    //    public IIncludableQueryContext<T, TPropertyEnumerable> ThenIncludeChildren<TProperty, TPropertyEnumerable>(
    //        Expression body,
    //        IReadOnlyCollection<ParameterExpression> parameters)
    //            where TProperty : class
    //            where TPropertyEnumerable : IEnumerable<TProperty>
    //    {
    //        var q = (IIncludableQueryable<T, IEnumerable<TP>>)queryable;
    //        var exp = Expression.Lambda<Func<TP, TPropertyEnumerable>>(body, parameters);
    //        var iq = q.ThenInclude(ReplaceEnumerable<TP, TPropertyEnumerable, TProperty>(exp));
    //        return new IncludableChildrenQueryContext<T, TPropertyEnumerable, TProperty>(db, iq, errorModel);
    //    }

    //    IIncludableQueryContext<T, TProperty> IIncludableQueryContext<T, ITP>.ThenInclude<TProperty>(LambdaExpression path)
    //    {
    //        if (typeof(TProperty).TryGetEnumerableItem(out var itemType))
    //        {
    //            var r = this.GetInstanceGenericMethod(nameof(ThenIncludeChildren), itemType, typeof(TProperty))
    //                .As<IIncludableQueryContext<T, TProperty>>()
    //                .Invoke(path.Body, path.Parameters);
    //            var rc = r as IIncludableQueryContext<T, TProperty>;
    //            return rc;
    //        }
    //        var q = (IIncludableQueryable<T, IEnumerable<TP>>)queryable;
    //        var exp = Expression.Lambda<Func<TP, TProperty>>(path.Body, path.Parameters);
    //        var iq = q.ThenInclude(Replace(exp));
    //        return new IncludableQueryContext<T, TProperty>(db, iq, errorModel);
    //    }
    //}

}
