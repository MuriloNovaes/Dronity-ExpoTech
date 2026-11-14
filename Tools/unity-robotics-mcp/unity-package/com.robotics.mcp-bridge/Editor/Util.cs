using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RoboticsMcp
{
    /// <summary>Acesso tipado aos parâmetros JSON de um comando.</summary>
    public class Args
    {
        readonly Dictionary<string, object> values;
        public Args(Dictionary<string, object> values) { this.values = values; }

        public bool Has(string key) => values.TryGetValue(key, out var v) && v != null;
        public object Raw(string key) => Has(key) ? values[key] : null;

        public string Str(string key, string fallback = null) => Has(key) ? Convert.ToString(values[key], CultureInfo.InvariantCulture) : fallback;
        public float Float(string key, float fallback = 0) => Has(key) ? Convert.ToSingle(values[key], CultureInfo.InvariantCulture) : fallback;
        public int Int(string key, int fallback = 0) => Has(key) ? Convert.ToInt32(values[key], CultureInfo.InvariantCulture) : fallback;
        public bool Bool(string key, bool fallback = false) => Has(key) ? Convert.ToBoolean(values[key], CultureInfo.InvariantCulture) : fallback;

        public string Required(string key)
        {
            if (!Has(key)) throw new ArgumentException($"Parâmetro obrigatório '{key}' ausente");
            return Str(key);
        }

        public Vector3? Vec3(string key) => Has(key) ? Util.ToVector3(values[key]) : (Vector3?)null;

        public List<object> List(string key) => Has(key) ? (List<object>)values[key] : new List<object>();

        public Dictionary<string, object> Dict(string key) =>
            Has(key) && values[key] is Dictionary<string, object> d ? d : new Dictionary<string, object>();
    }

    public static class Util
    {
        public static Vector3 ToVector3(object value)
        {
            var list = (IList)value;
            if (list.Count != 3) throw new ArgumentException("Vetor deve ter 3 componentes");
            return new Vector3(
                Convert.ToSingle(list[0], CultureInfo.InvariantCulture),
                Convert.ToSingle(list[1], CultureInfo.InvariantCulture),
                Convert.ToSingle(list[2], CultureInfo.InvariantCulture));
        }

        /// <summary>Encontra GameObject por caminho ("Robo/base_link") ou por nome, incluindo inativos.</summary>
        public static GameObject Find(string pathOrName)
        {
            if (string.IsNullOrEmpty(pathOrName)) throw new ArgumentException("Caminho do objeto vazio");
            var found = GameObject.Find(pathOrName);
            if (found != null) return found;

            var parts = pathOrName.Trim('/').Split('/');
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (parts.Length > 1 && root.name == parts[0])
                    {
                        var t = root.transform.Find(string.Join("/", parts.Skip(1)));
                        if (t != null) return t.gameObject;
                    }
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        if (t.name == parts[parts.Length - 1]) return t.gameObject;
                }
            }
            throw new ArgumentException($"GameObject '{pathOrName}' não encontrado na cena");
        }

        public static string PathOf(Transform t)
        {
            var names = new List<string>();
            for (; t != null; t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }

        public static Type FindType(string name)
        {
            Type match = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (var t in types)
                {
                    if (t.FullName == name) return t;
                    if (match == null && t.Name == name) match = t;
                }
            }
            return match;
        }

        public static Type RequireType(string fullName, string package)
        {
            var t = FindType(fullName);
            if (t == null) throw new InvalidOperationException($"Tipo {fullName} não encontrado. Instale o pacote {package} (unity_install_robotics_packages).");
            return t;
        }

        public static bool HasType(string fullName) => FindType(fullName) != null;

        /// <summary>Converte valor JSON para o tipo do campo/propriedade (inclui referências por caminho).</summary>
        public static object Coerce(object value, Type target)
        {
            if (value == null) return null;
            if (target.IsInstanceOfType(value)) return value;
            if (target == typeof(Vector3)) return ToVector3(value);
            if (target == typeof(Quaternion)) return Quaternion.Euler(ToVector3(value));
            if (target == typeof(Color) && value is IList c)
                return new Color(Convert.ToSingle(c[0]), Convert.ToSingle(c[1]), Convert.ToSingle(c[2]), c.Count > 3 ? Convert.ToSingle(c[3]) : 1f);
            if (target.IsEnum) return Enum.Parse(target, Convert.ToString(value), true);
            if (target == typeof(GameObject)) return Find((string)value);
            if (typeof(Component).IsAssignableFrom(target))
            {
                var go = Find((string)value);
                var comp = go.GetComponent(target);
                if (comp == null) throw new ArgumentException($"'{value}' não tem componente {target.Name}");
                return comp;
            }
            if (typeof(Object).IsAssignableFrom(target) && value is string assetPath)
                return AssetDatabase.LoadAssetAtPath(assetPath, target);
            if (target.IsArray && value is IList items)
            {
                var elementType = target.GetElementType();
                var array = Array.CreateInstance(elementType, items.Count);
                for (int i = 0; i < items.Count; i++) array.SetValue(Coerce(items[i], elementType), i);
                return array;
            }
            return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
        }

        const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Define um campo ou propriedade por reflexão. Retorna false se não existir.</summary>
        public static bool SetMember(object target, string name, object value)
        {
            var type = target.GetType();
            var prop = type.GetProperty(name, Members);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(target, Coerce(value, prop.PropertyType));
                return true;
            }
            var field = type.GetField(name, Members);
            if (field != null)
            {
                field.SetValue(target, Coerce(value, field.FieldType));
                return true;
            }
            return false;
        }

        public static object GetMember(object target, string name)
        {
            var type = target.GetType();
            var prop = type.GetProperty(name, Members);
            if (prop != null && prop.CanRead) return prop.GetValue(target);
            var field = type.GetField(name, Members);
            return field?.GetValue(target);
        }

        /// <summary>
        /// Procura um tipo pelo nome curto dentro de um prefixo de namespace (os pacotes do
        /// Robotics Hub mudaram namespaces entre versões).
        /// </summary>
        public static Type FindInNamespace(string shortName, string namespacePrefix)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (var t in types)
                    if (t.Name == shortName && t.Namespace != null && t.Namespace.StartsWith(namespacePrefix))
                        return t;
            }
            return null;
        }

        /// <summary>
        /// Chama um método estático passando os primeiros argumentos por posição; os demais
        /// parâmetros usam seus valores default. Tolera mudanças de assinatura entre versões.
        /// </summary>
        public static object InvokeLoose(Type type, string method, params object[] leading)
        {
            var mi = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == method)
                .Where(m =>
                {
                    var ps = m.GetParameters();
                    if (ps.Length < leading.Length) return false;
                    for (int i = 0; i < ps.Length; i++)
                    {
                        if (i < leading.Length) { if (leading[i] != null && !ps[i].ParameterType.IsInstanceOfType(leading[i])) return false; }
                        else if (!ps[i].HasDefaultValue) return false;
                    }
                    return true;
                })
                .OrderBy(m => m.GetParameters().Length)
                .FirstOrDefault();
            if (mi == null)
                throw new MissingMethodException($"{type.FullName}.{method} compatível não encontrado nesta versão do pacote");
            var ps2 = mi.GetParameters();
            var args = new object[ps2.Length];
            for (int i = 0; i < ps2.Length; i++) args[i] = i < leading.Length ? leading[i] : ps2[i].DefaultValue;
            return mi.Invoke(null, args);
        }

        public static void MarkSceneDirty(GameObject go)
        {
            if (!Application.isPlaying && go.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }
}
