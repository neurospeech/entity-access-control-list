#nullable enable
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace NeuroSpeech.EntityAccessControl
{

    //[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    //public class DbFunctionAttribute: Attribute
    //{

    //}

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class ExternalFunctionAttribute: Attribute
    {

    }



    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class ExternalMethodAttribute : Attribute
    {

    }


    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public class CompositeKeyAttribute: Attribute
    {
        public readonly string[] Names;

        public CompositeKeyAttribute(params string[] names)
        {
            this.Names = names;
        }

    }

    public static class CompositeKeyAttributeExtensions
    {
        public static void RegisterCompositeKeys(this ModelBuilder modelBuilder)
        {
            foreach(var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var ck = entityType.ClrType.GetCustomAttribute<CompositeKeyAttribute>();
                if (ck == null)
                    continue;
                modelBuilder.Entity(entityType.ClrType, (a) => {
                    a.HasKey(ck.Names);
                });
            }
        }
    }
}
