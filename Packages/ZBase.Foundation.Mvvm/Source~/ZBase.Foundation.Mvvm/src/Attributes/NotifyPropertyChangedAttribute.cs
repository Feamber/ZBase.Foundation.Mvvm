﻿using System;

namespace ZBase.Foundation.Mvvm
{
    /// <summary>
    /// An attribute that indicates that a given property will notify clients when its value is changed.
    /// </summary>
    /// <remarks>
    /// This attribute is not intended to be used directly by user code to decorate user-defined types.
    /// <br/>
    /// However, it can be used in other contexts, such as reflection.
    /// </remarks>
    /// <seealso cref="INotifyPropertyChanged"/>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class NotifyPropertyChangedAttribute : Attribute
    {
        public string PropertyName { get; }

        public Type PropertyType { get; }

        public NotifyPropertyChangedAttribute(string propertyName, Type propertyType)
        {
            this.PropertyName = propertyName;
            this.PropertyType = propertyType;
        }
    }
}
