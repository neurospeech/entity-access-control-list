﻿using Microsoft.EntityFrameworkCore;
using NeuroSpeech.EntityAccessControl.Security;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace NeuroSpeech.EntityAccessControl
{

    public interface IQueryContext
    {
        IQueryContext<T> OfType<T>();
    } 

    public interface IQueryContext<T>: IQueryContext
    {
        IQueryContext<T> Where(Expression<Func<T, bool>> filter);
        IQueryContext<T1> Set<T1>() where T1: class;

        IQueryable<T> ToQuery();

    }

    public readonly struct QueryContext<T>: IQueryContext<T>
    {
        private readonly ISecureRepository db;
        private readonly IQueryable<T> queryable;

        public QueryContext(ISecureRepository db, IQueryable<T> queryable)
        {
            this.db = db;
            this.queryable = queryable;
        }

        public IQueryContext<T1> Set<T1>()
            where T1: class
        {
            return new QueryContext<T1>(db, db.Query<T1>());
        }

        public IQueryContext<T> Where(Expression<Func<T, bool>> filter)
        {
            return new QueryContext<T>(db, queryable.Where(filter));
        }

        public IQueryable<T> ToQuery()
        {
            return queryable;
        }

        public IQueryContext<T1> OfType<T1>()
        {
            return new QueryContext<T1>(db, queryable.OfType<T1>());
        }
    }

    public abstract class BaseSecurityRules<TC>
    {
        private RulesDictionary select = new RulesDictionary();
        private RulesDictionary insert = new RulesDictionary();
        private RulesDictionary update = new RulesDictionary();
        private RulesDictionary delete = new RulesDictionary();
        private RulesDictionary modify = new RulesDictionary();

        private Dictionary<Type, Func<object,object>> selectMapper
            = new Dictionary<Type, Func<object, object>>();

        internal IQueryable<T> Apply<T>(IQueryContext<T> ts, TC client) where T : class
        {
            return select.As<T, TC>()(ts, client).ToQuery();
        }

        internal IQueryable<T> ApplyInsert<T>(IQueryContext<T> q, TC client) where T : class
        {
            return insert.As<T, TC>()(q, client).ToQuery();
        }

        internal IQueryable<T> ApplyDelete<T>(IQueryContext<T> q, TC client) where T : class
        {
            return delete.As<T, TC>()(q, client).ToQuery();
        }

        internal IQueryable<T> ApplyUpdate<T>(IQueryContext<T> q, TC client) where T : class
        {
            return update.As<T,TC>()(q, client).ToQuery();
        }

        /**
         * This will be called before serializing object of given type
         */
        public void Map<T>(Func<T,object> mapper)
        {
            selectMapper[typeof(T)] = (x) => mapper((T)x);
        }

        internal object? MapObject(object item)
        {
            if(selectMapper.TryGetValue(item.GetType(), out var mapper))
            {
                return mapper(item);
            }
            return item;
        }

        /// <summary>
        /// Set filter for select, insert, update, delete
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="select"></param>
        /// <param name="insert"></param>
        /// <param name="update"></param>
        /// <param name="delete"></param>
        protected void SetFilters<T>(
            Func<IQueryContext<T>, TC, IQueryContext<T>>? select = null,
            Func<IQueryContext<T>, TC, IQueryContext<T>>? insert = null,
            Func<IQueryContext<T>, TC, IQueryContext<T>>? update = null,
            Func<IQueryContext<T>, TC, IQueryContext<T>>? delete = null)
        {
            if (select != null)
                this.select.SetFunc<T, TC>(select);
            if (insert != null)
                this.insert.SetFunc<T, TC>(insert);
            if (update != null)
                this.update.SetFunc<T, TC>(update);
            if (delete != null)
                this.delete.SetFunc<T, TC>(delete);
        }

        /// <summary>
        /// Set one filter for all (select, insert, update, delete)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="all"></param>
        public void SetAllFilters<T>(
            Func<IQueryContext<T>, TC, IQueryContext<T>> all)
        {
            SetFilters<T>(all, all, all, all);
        }


        public static IQueryContext<T> Allow<T>(IQueryContext<T> q, TC c) => q;

        public static IQueryContext<T> Unauthorized<T>(IQueryContext<T> q, TC c)
                   where T : class
                   => throw new UnauthorizedAccessException();

        internal void VerifyModifyMember<T>(PropertyInfo propertyInfo, TC c)
        {
            throw new NotImplementedException();
        }
    }
}
