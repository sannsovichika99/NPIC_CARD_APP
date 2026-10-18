using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace NPIC_CARD_APP
{
    /// <summary>
    /// Property lists (PropertyGrid in Card Text… and Card Designer…) in the app's language: the names, descriptions and
    /// categories written in the [DisplayName] / [Description] / [Category] attributes, and enum choices ("Center", "AsIs"),
    /// are looked up in the Khmer table (<see cref="Lang.T(string)"/>). Only the display changes: saved files and the
    /// values in code keep the English property and enum names.
    /// </summary>
    internal static class LocalizedProperties
    {
        private static readonly HashSet<Type> Done = new HashSet<Type>();

        /// <summary>Shows the properties of these types (and their base types) translated. Does nothing in English.</summary>
        public static void Register(params Type[] types)
        {
            if (!Lang.Khmer) return;
            foreach (Type t in types)
            {
                if (t == null || !Done.Add(t)) continue;
                TypeDescriptor.AddProvider(new Provider(TypeDescriptor.GetProvider(t)), t);
            }
        }

        private sealed class Provider : TypeDescriptionProvider
        {
            public Provider(TypeDescriptionProvider parent) : base(parent) { }

            public override ICustomTypeDescriptor GetTypeDescriptor(Type objectType, object instance)
            {
                return new Descriptor(base.GetTypeDescriptor(objectType, instance));
            }
        }

        private sealed class Descriptor : CustomTypeDescriptor
        {
            public Descriptor(ICustomTypeDescriptor parent) : base(parent) { }

            public override PropertyDescriptorCollection GetProperties()
            {
                return Wrap(base.GetProperties());
            }

            public override PropertyDescriptorCollection GetProperties(Attribute[] attributes)
            {
                return Wrap(base.GetProperties(attributes));
            }

            private static PropertyDescriptorCollection Wrap(PropertyDescriptorCollection properties)
            {
                return new PropertyDescriptorCollection(
                    properties.Cast<PropertyDescriptor>().Select(p => (PropertyDescriptor)new Property(p)).ToArray(), true);
            }
        }

        /// <summary>A property shown with translated texts; reading and writing go to the real property.</summary>
        private sealed class Property : PropertyDescriptor
        {
            private readonly PropertyDescriptor _inner;
            private TypeConverter _converter;

            public Property(PropertyDescriptor inner) : base(inner)
            {
                _inner = inner;
            }

            public override string DisplayName { get { return Lang.T(_inner.DisplayName); } }
            public override string Description { get { return Lang.T(_inner.Description); } }
            public override string Category { get { return Lang.T(_inner.Category); } }

            public override TypeConverter Converter
            {
                get
                {
                    if (_converter == null)
                    {
                        TypeConverter inner = _inner.Converter;
                        _converter = inner != null && inner.GetType() == typeof(EnumConverter) && PropertyType.IsEnum
                            ? new EnumNames(PropertyType)
                            : inner;
                    }
                    return _converter;
                }
            }

            public override Type ComponentType { get { return _inner.ComponentType; } }
            public override bool IsReadOnly { get { return _inner.IsReadOnly; } }
            public override Type PropertyType { get { return _inner.PropertyType; } }
            public override bool CanResetValue(object component) { return _inner.CanResetValue(component); }
            public override object GetValue(object component) { return _inner.GetValue(component); }
            public override void ResetValue(object component) { _inner.ResetValue(component); }
            public override void SetValue(object component, object value) { _inner.SetValue(component, value); }
            public override bool ShouldSerializeValue(object component) { return _inner.ShouldSerializeValue(component); }
            public override object GetEditor(Type editorBaseType) { return _inner.GetEditor(editorBaseType); }
            public override bool SupportsChangeEvents { get { return _inner.SupportsChangeEvents; } }
            public override void AddValueChanged(object component, EventHandler handler) { _inner.AddValueChanged(component, handler); }
            public override void RemoveValueChanged(object component, EventHandler handler) { _inner.RemoveValueChanged(component, handler); }
        }

        /// <summary>Enum choices with translated names ("Center" → "កណ្ដាល"); the value itself is unchanged.</summary>
        private sealed class EnumNames : EnumConverter
        {
            public EnumNames(Type type) : base(type) { }

            public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
            {
                if (destinationType == typeof(string) && value != null && value.GetType() == EnumType)
                    return Lang.T(Enum.GetName(EnumType, value) ?? Convert.ToString(value, CultureInfo.InvariantCulture));
                return base.ConvertTo(context, culture, value, destinationType);
            }

            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                var text = value as string;
                if (text != null)
                    foreach (string name in Enum.GetNames(EnumType))
                        if (string.Equals(Lang.T(name), text.Trim(), StringComparison.CurrentCultureIgnoreCase))
                            return Enum.Parse(EnumType, name);
                return base.ConvertFrom(context, culture, value);
            }
        }
    }
}
