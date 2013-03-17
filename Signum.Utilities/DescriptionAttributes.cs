﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Resources;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Signum.Utilities;
using System.Reflection;
using Signum.Utilities.Reflection;
using System.Linq.Expressions;
using Signum.Utilities.ExpressionTrees;
using System.Collections.Concurrent;
using System.IO;
using System.Xml.Linq;


namespace Signum.Utilities
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property |AttributeTargets.Field | AttributeTargets.Enum | AttributeTargets.Interface, Inherited = true)]
    public class DescriptionOptionsAttribute : Attribute
    {
        public DescriptionOptions Options { get; set; }

        public DescriptionOptionsAttribute(DescriptionOptions options)
        {
            this.Options = options;
        }
    }

    public enum DescriptionOptions
    {
        None = 0,

        Members = 1,
        Description = 2,
        PluralDescription = 4,
        Gender = 8,

        All = Members | Description | PluralDescription | Gender,
    }

    static class DescriptionOptionsExtensions
    {
        public static bool IsSetAssert(this DescriptionOptions opts, DescriptionOptions flag, MemberInfo member)
        {
            if ((opts.IsSet(DescriptionOptions.PluralDescription) || opts.IsSet(DescriptionOptions.Gender)) && !opts.IsSet(DescriptionOptions.Description))
                throw new InvalidOperationException("{0} has {1} set also requires {2}".Formato(member.Name, opts, DescriptionOptions.Description));

            if ((member is PropertyInfo || member is FieldInfo) &&
                (opts.IsSet(DescriptionOptions.PluralDescription) ||
                 opts.IsSet(DescriptionOptions.Gender) ||
                 opts.IsSet(DescriptionOptions.Members)))
                throw new InvalidOperationException("Member {0} has {1} set".Formato(member.Name, opts));

            return opts.IsSet(flag);
        }

        public static bool IsSet(this DescriptionOptions opts, DescriptionOptions flag)
        {
            return (opts & flag) == flag;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class PluralDescriptionAttribute : Attribute
    {
        public string PluralDescription { get; private set; }

        public PluralDescriptionAttribute(string pluralDescription)
        {
            this.PluralDescription = pluralDescription;
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class GenderAttribute : Attribute
    {
        public char Gender { get; set; }

        public GenderAttribute(char gender)
        {
            this.Gender = gender;
        }
    }

    [AttributeUsage(AttributeTargets.Assembly, Inherited = true)]
    public class DefaultAssemblyCultureAttribute : Attribute
    {
        public string DefaultCulture { get; private set; }

        public DefaultAssemblyCultureAttribute(string defaultCulture)
        {
            this.DefaultCulture = defaultCulture;
        }
    }



    public static class DescriptionManager
    {
        public static Func<Type, string> CleanTypeName = t => t.Name; //To allow MyEntityDN
        public static Func<Type, Type> CleanType = t => t; //To allow Lite<T>

        public static string TranslationDirectory = Path.Combine(Path.GetDirectoryName(new Uri(typeof(DescriptionManager).Assembly.CodeBase).LocalPath), "Translations");

        public static event Func<Type, DescriptionOptions?> DefaultDescriptionOptions = t => t.Name.EndsWith("Message") ? DescriptionOptions.Members : (DescriptionOptions?)null;


        public static string NiceName(this Type type)
        {
            type = CleanType(type);

            var result = Fallback(type, lt => lt.Description);

            if (result == null)
                throw new InvalidOperationException("Description not found on {0}".Formato(type.Name));

            return result;
        }

        static string Fallback(Type type,  Func<LocalizedType, string> typeValue)
        {
            var cc = CultureInfo.CurrentUICulture;
            {
                var loc = GetLocalizedType(type, cc);
                if (loc != null)
                {
                    string result = typeValue(loc);
                    if (result != null)
                        return result;
                }
            }

            if (cc.Parent.Name.HasText())
            {
                var loc = GetLocalizedType(type, cc.Parent);
                if (loc != null)
                {
                    string result = typeValue(loc);
                    if (result != null)
                        return result;
                }
            }

            var global = CultureInfo.GetCultureInfo(LocalizedAssembly.GetDefaultAssemblyCulture(type.Assembly));
            {
                var loc = GetLocalizedType(type, global);
                if (loc == null)
                    throw new InvalidOperationException("Type {0} is not localizable".Formato(type.TypeName()));

                return typeValue(loc);
            }
        }

        public static string NicePluralName(this Type type)
        {
            type = CleanType(type);

            var result = Fallback(type, lt => lt.PluralDescription);

            if (result == null)
                throw new InvalidOperationException("PluralDescription not found on {0}".Formato(type.Name));

            return result;
        }

        public static string NiceToString(this Enum a)
        {
            var fi = EnumFieldCache.Get(a.GetType()).TryGetC(a);
            if (fi != null)
                return GetMemberNiceName(fi);

            return a.ToString().NiceName();
        }

        public static string NiceName(this PropertyInfo pi)
        {
            return GetMemberNiceName(pi) ??
                (pi.IsDefaultName() ? pi.PropertyType.NiceName() : pi.Name.NiceName());
        }

        public static bool IsDefaultName(this PropertyInfo pi)
        {
            return pi.Name == CleanTypeName(CleanType(pi.PropertyType)); 
        }

        static string GetMemberNiceName(MemberInfo memberInfo)
        {
            if (memberInfo.DeclaringType == typeof(DayOfWeek))
            {
                return CultureInfo.CurrentCulture.DateTimeFormat.DayNames[(int)((FieldInfo)memberInfo).GetValue(null)];
            }

            var cc = CultureInfo.CurrentUICulture;

            var type = memberInfo.DeclaringType;

            var result = Fallback(type, lt => lt.Members.TryGetC(memberInfo.Name));

            return result;
        }

        public static char? GetGender(this Type type)
        {
            type = CleanType(type);

            var cc = CultureInfo.CurrentUICulture;

            return GetLocalizedType(type, cc).TryCS(lt => lt.Gender) ??
                (cc.Parent.Name.HasText() ? GetLocalizedType(type, cc.Parent).TryCS(lt => lt.Gender) : null);
        }

        static ConcurrentDictionary<CultureInfo, ConcurrentDictionary<Assembly, LocalizedAssembly>> localizations = 
            new ConcurrentDictionary<CultureInfo, ConcurrentDictionary<Assembly, LocalizedAssembly>>();

        public static LocalizedType GetLocalizedType(Type type, CultureInfo cultureInfo)
        {
            return GetLocalizedAssembly(type.Assembly, cultureInfo).Types.TryGetC(type); 
        }

        public static LocalizedAssembly GetLocalizedAssembly(Assembly assembly, CultureInfo cultureInfo)
        {
            return localizations
                .GetOrAdd(cultureInfo, ci => new ConcurrentDictionary<Assembly, LocalizedAssembly>())
                .GetOrAdd(assembly, (Assembly a) => LocalizedAssembly.ImportXml(assembly, cultureInfo));
        }

        internal static DescriptionOptions? OnDefaultDescriptionOptions(Type type)
        {
            if (DescriptionManager.DefaultDescriptionOptions == null)
                return null;

            foreach (Func<Type, DescriptionOptions?> action in DescriptionManager.DefaultDescriptionOptions.GetInvocationList())
            {
                var result = action(type);
                if (result != null)
                    return result.Value;
            }

            return null;
        }
    }


    public class LocalizedAssembly
    {
        public Assembly Assembly;
        public CultureInfo Culture;
        public bool IsDefault;

        private LocalizedAssembly() { }

        public Dictionary<Type, LocalizedType> Types = new Dictionary<Type, LocalizedType>();

        static string TranslationFileName(Assembly assembly, CultureInfo cultureInfo)
        {
            return Path.Combine(DescriptionManager.TranslationDirectory, "{0}.{1}.xml".Formato(assembly.GetName().Name, cultureInfo.Name));
        }

        static DescriptionOptions GetDescriptionOptions(Type type)
        {
            var doa = type.SingleAttributeInherit<DescriptionOptionsAttribute>();
            if (doa != null)
                return doa.Options;

            DescriptionOptions? def = DescriptionManager.OnDefaultDescriptionOptions(type);
            if (def != null)
                return def.Value;

            if (type.IsEnum)
                return DescriptionOptions.Members | DescriptionOptions.Description;

            return DescriptionOptions.None;
        }

        public static string GetDefaultAssemblyCulture(Assembly assembly)
        {
            var defaultLoc = assembly.SingleAttribute<DefaultAssemblyCultureAttribute>();

            if (defaultLoc == null)
                throw new InvalidOperationException("Assembly {0} does not have {1}".Formato(assembly.GetName().Name, typeof(DefaultAssemblyCultureAttribute).Name));

            return defaultLoc.DefaultCulture;
        }

        public void ExportXml()
        {
            var doc = new XDocument(new XDeclaration("1.0", "UTF8", "yes"),
                new XElement("Translations",
                    from kvp in Types
                    let type = kvp.Key
                    let lt = kvp.Value
                    let doa = GetDescriptionOptions(type)
                    where doa != DescriptionOptions.None
                    select lt.ExportXml()
                )
            );

            string fileName = TranslationFileName(Assembly, Culture);

            doc.Save(fileName);
        }

        public static LocalizedAssembly ImportXml(Assembly assembly, CultureInfo cultureInfo)
        {
            bool isDefault = cultureInfo.Name == GetDefaultAssemblyCulture(assembly);

            string fileName = TranslationFileName(assembly, cultureInfo);

            Dictionary<string, XElement> file = !File.Exists(fileName) ? null :
                XDocument.Load(fileName).Element("Translations").Elements("Type")
                .Select(x => KVP.Create(x.Attribute("Name").Value, x))
                .Distinct(x => x.Key)
                .ToDictionary();

            if (!isDefault && file == null)
                return null;

            var result = new LocalizedAssembly
            {
                Assembly = assembly,
                Culture = cultureInfo,
                IsDefault = isDefault
            };

            result.Types = (from t in assembly.GetTypes()
                            let opts = GetDescriptionOptions(t)
                            where opts != DescriptionOptions.None
                            let x = file.TryGetC(t.Name)
                            select LocalizedType.ImportXml(t, opts, result, x))
                            .ToDictionary(lt => lt.Type);

            return result;
        }
    }

    public class LocalizedType
    {
        public Type Type { get; private set; }
        public LocalizedAssembly Assembly { get; private set; }
        public DescriptionOptions Options { get; private set; }

        public string Description { get; set; }
        public string PluralDescription { get; set; }
        public char? Gender { get; set; }

        public Dictionary<string, string> Members = new Dictionary<string, string>();

        LocalizedType() { }

        public XElement ExportXml()
        {
            return new XElement("Type",
                    new XAttribute("Name", Type.Name),

                    !Options.IsSetAssert(DescriptionOptions.Description, Type) ||
                    Description == null ||
                    (Assembly.IsDefault && Description == (Type.SingleAttribute<DescriptionAttribute>().TryCC(t => t.Description) ?? DescriptionManager.CleanTypeName(Type).SpacePascal())) ? null :
                    new XAttribute("Description", Description),

                    !Options.IsSetAssert(DescriptionOptions.PluralDescription, Type) ||
                    PluralDescription == null ||
                    (PluralDescription == NaturalLanguageTools.Pluralize(Description, Assembly.Culture)) ? null :
                    new XAttribute("PluralDescription", PluralDescription),

                    !Options.IsSetAssert(DescriptionOptions.Gender, Type) ||
                    Gender == null ||
                    (Gender == NaturalLanguageTools.GetGender(Description, Assembly.Culture)) ? null :
                    new XAttribute("Gender", Gender),

                    !Options.IsSetAssert(DescriptionOptions.Members, Type) ? null :
                     (from m in Type.GetProperties(bf).Cast<MemberInfo>().Concat(Type.GetFields(bf))
                      let doam = m.SingleAttribute<DescriptionOptionsAttribute>()
                      where doam == null || doam.Options.IsSetAssert(DescriptionOptions.Description, m)
                      let value = Members.TryGetC(m.Name)
                      where value != null && (!Assembly.IsDefault || ((Type.SingleAttribute<DescriptionAttribute>().TryCC(t => t.Description) ?? m.Name.NiceName()) != value))
                      select new XElement("Member", new XAttribute("MemberName", m.Name), new XAttribute("Name", value)))
                );
        }

        const BindingFlags bf = BindingFlags.Public | BindingFlags.Instance;

        static IEnumerable<MemberInfo> GetMembers(Type type)
        {
            if (type.IsEnum)
                return EnumFieldCache.Get(type).Values;
            else
                return type.GetProperties(bf).Concat(type.GetFields(bf).Cast<MemberInfo>());
        }

        internal static LocalizedType ImportXml(Type type, DescriptionOptions opts, LocalizedAssembly assembly, XElement x)
        {
            string name = !opts.IsSetAssert(DescriptionOptions.Description, type) ? null :
                (x == null ? null : x.Attribute("Description").TryCC(xa => xa.Value)) ??
                (!assembly.IsDefault ? null : DefaultTypeDescription(type));

            var xMembers = x == null ? null : x.Elements("Member")
                .Select(m => KVP.Create(m.Attribute("Name").Value, m.Attribute("Description").Value))
                .Distinct(m => m.Key)
                .ToDictionary();

            LocalizedType result = new LocalizedType
            {
                Type = type,
                Options = opts,
                Assembly = assembly,

                Description = name,
                PluralDescription = !opts.IsSetAssert(DescriptionOptions.PluralDescription, type) ? null :
                             ((x == null ? null : x.Attribute("PluralDescription").TryCC(xa => xa.Value)) ??
                             (!assembly.IsDefault ? null : type.SingleAttribute<PluralDescriptionAttribute>().TryCC(t => t.PluralDescription)) ??
                             (name == null ? null : NaturalLanguageTools.Pluralize(name, assembly.Culture))),

                Gender = !opts.IsSetAssert(DescriptionOptions.Gender, type) ? null :
                         ((x == null ? null : x.Attribute("Gender").TryCS(xa => xa.Value.Single())) ??
                         (!assembly.IsDefault ? null : type.SingleAttribute<GenderAttribute>().TryCS(t => t.Gender)) ??
                         (name == null ? null : NaturalLanguageTools.GetGender(name, assembly.Culture))),

                Members = !opts.IsSetAssert(DescriptionOptions.Members, type) ? null :
                          (from m in GetMembers(type)
                           let mta = m.SingleAttribute<DescriptionOptionsAttribute>()
                           where mta == null || mta.Options.IsSetAssert(DescriptionOptions.Description, m)
                           let value = xMembers.TryGetC(m.Name) ?? (!assembly.IsDefault ? null : DefaultMemberDescription(m))
                           where value != null
                           select KVP.Create(m.Name, value))
                           .ToDictionary()
            };

            return result;
        }

        static string DefaultTypeDescription(Type type)
        {
            return type.SingleAttribute<DescriptionAttribute>().TryCC(t => t.Description) ?? DescriptionManager.CleanTypeName(type).SpacePascal();
        }

        static string DefaultMemberDescription(MemberInfo m)
        {
            return (m.SingleAttribute<DescriptionAttribute>().TryCC(t => t.Description) ?? m.Name.NiceName());
        }
    }

}
