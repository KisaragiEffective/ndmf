using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
#if VRCHAT_AVATARS_PRESENT
using HarmonyLib;
using UnityEngine.Profiling;
#endif
using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace nadena.dev.ndmf.VRChat
{
    internal static class PropertyChangeTracker
    {
#if VRCHAT_AVATARS_PRESENT
        private static Harmony harmony;
        
        [InitializeOnLoadMethod]
        private static void Prepare()
        {
            // TODO: 本PRを出すときに変える
            harmony = new Harmony("io.github.kisaragieffective.ndmf.experimental");
            ApplyPatches();
            AssemblyReloadEvents.beforeAssemblyReload += () => { harmony.UnpatchAll(); };
        }
        
        // TODO: ビルド中のみパッチを当てる
        internal static void ApplyPatches()
        {
            TransformPositionPreSet.Patch(harmony);
            GameObjectMovePre.Patch(harmony);
            GameObjectDestroyPre.Patch(harmony);
            RendererMaterialChangePre.Patch(harmony);
            AddComponentPre.Patch(harmony);
        }

        internal static void UnpatchAll()
        {
            harmony.UnpatchAll();
        }

        private static IEnumerable<StackFrame> GetEnumerableStackTrace()
        {
            Profiler.BeginSample("GetEnumerableStackTrace");
            var st = new StackTrace();
            var ret = Enumerable.Range(0, st.FrameCount).Select(st.GetFrame);
            Profiler.EndSample();
            return ret;
        }

        [CanBeNull]
        internal static Type GetInnermostPass() {
            Profiler.BeginSample("GetInnermostPass");
            var ret = GetEnumerableStackTrace()
                .Select(x => x.GetMethod().DeclaringType)
                .Where(x => x is not null)
                // AnonymousPassということもありえる
                .FirstOrDefault(x => x.GetInterface(nameof(IPass)) is not null);
            Profiler.EndSample();
            return ret;
        }

        internal static void ReportExternMethod(Type receiver, string name, params Type[] argumentTypes)
        {
            // TODO: 本PR出すときに多分消す
            var argSignatures = string.Join(", ", argumentTypes.Select(x => x.Name));
            Debug.Log($"MajorPropertyPreEditHook: {receiver}.{name}({argSignatures}) is not supported because it is an extern method.");
        }
    }
    
    internal static class TransformPositionPreSet 
    {
        internal static void Patch(Harmony h)
        {
            var t = typeof(Transform);
            var setter = AccessTools.PropertySetter(t, nameof(Transform.position));
            var mw =
                new HarmonyMethod(
                    typeof(TransformPositionPreSet).GetMethod(nameof(prefix), BindingFlags.Static | BindingFlags.NonPublic)
                );
            h.Patch(original: setter, prefix: mw);
        }

        private static void prefix(Transform __instance, Vector3 __0) 
        {
            var propertyModifiedPass = PropertyChangeTracker.GetInnermostPass();

            if (propertyModifiedPass != null)
            {
                Debug.Log($"transform: {__instance} := {__0} by {propertyModifiedPass}");
            }
        }
    }

    /// <summary>
    /// Tracks <see cref="GameObject"/>'s <see cref="Transform"/> change.
    /// </summary>
    internal static class GameObjectMovePre 
    {
        internal static void Patch(Harmony h)
        {
            var t = typeof(Transform);
            {
                var m = AccessTools.Method(t, nameof(Transform.SetParent), new []{ typeof(Transform) });
                var mw = new HarmonyMethod(typeof(GameObjectMovePre).GetMethod(nameof(prefixOnMethodWithoutReparentArg),
                    BindingFlags.Static | BindingFlags.NonPublic));

                h.Patch(original: m, prefix: mw);
            }

            {
                var m = AccessTools.Method(t, nameof(Transform.SetParent), new []{ typeof(Transform), typeof(bool) });
                var mw = new HarmonyMethod(typeof(GameObjectMovePre).GetMethod(nameof(prefixOnMethodWithReparentArg),
                    BindingFlags.Static | BindingFlags.NonPublic));

                PropertyChangeTracker.ReportExternMethod(typeof(Transform), nameof(Transform.SetParent), typeof(Transform), typeof(bool));
                // h.Patch(original: m, prefix: mw);
            }
            
            {
                var setter = AccessTools.PropertySetter(t, nameof(Transform.parent));
                var mw = new HarmonyMethod(typeof(GameObjectMovePre).GetMethod(nameof(prefixOnSetter),
                    BindingFlags.Static | BindingFlags.NonPublic));

                h.Patch(original: setter, prefix: mw);
            }
        }

        private static void prefixOnMethodWithoutReparentArg(Transform __instance, Transform p)
        {
            var pass = PropertyChangeTracker.GetInnermostPass();
            if (pass != null)
            {
                Debug.Log($"Transform: {__instance} is reparented under {p} by {pass}");
            }
        }
        
        private static void prefixOnMethodWithReparentArg(Transform __instance, Transform parent, bool worldPositionStays)
        {
            var pass = PropertyChangeTracker.GetInnermostPass();
            if (pass != null)
            {
                Debug.Log($"Transform: {__instance} is reparented under {parent}, {(worldPositionStays ? "kept" : "without keeping")} global transform by {pass}");
            }
        }
        
        private static void prefixOnSetter(Transform __instance, Transform __0)
        {
            var pass = PropertyChangeTracker.GetInnermostPass();
            if (pass != null)
            {
                Debug.Log($"Transform: {__instance} is reparented under {__0} by {pass}");
            }
        }
    }

    /// <summary>
    /// Tracks <see cref="GameObject.Destroy(Object)" /> and <see cref="GameObject.DestroyImmediate(Object)" />.
    /// </summary>
    internal static class GameObjectDestroyPre 
    {
        internal static void Patch(Harmony h)
        {
            var t = typeof(GameObject);
            {
                var m = AccessTools.Method(t, nameof(GameObject.Destroy), new []{ typeof(Object) });
                var mw = new HarmonyMethod(typeof(GameObjectDestroyPre).GetMethod(nameof(prefixOnDestroyWithoutDelayArgument),
                    BindingFlags.Static | BindingFlags.NonPublic));

                h.Patch(original: m, prefix: mw);
            }

            {
                var m = AccessTools.Method(t, nameof(GameObject.DestroyImmediate), new []{ typeof(Object) });
                var mw = new HarmonyMethod(typeof(GameObjectDestroyPre).GetMethod(nameof(prefixOnDestroyImmediateWithoutAllowDestroyAsset),
                    BindingFlags.Static | BindingFlags.NonPublic));
                
                h.Patch(original: m, prefix: mw);
            }
        }

        private static void prefixOnDestroyWithoutDelayArgument(GameObject obj)
        {
            var propertyModifiedPass = PropertyChangeTracker.GetInnermostPass();

            if (propertyModifiedPass != null)
            {
                Debug.Log($"GameObject: {obj} was destroyed by {propertyModifiedPass}");
            }
        }

        private static void prefixOnDestroyImmediateWithoutAllowDestroyAsset(GameObject obj)
        {
            var propertyModifiedPass = PropertyChangeTracker.GetInnermostPass();

            if (propertyModifiedPass != null)
            {
                Debug.Log($"GameObject: {obj} was destroyed immediately by {propertyModifiedPass}");
            }
        }
    }

    /// <summary>
    /// Tracks <see cref="Renderer.materials"/> change.
    /// </summary>
    internal static class RendererMaterialChangePre
    {
        internal static void Patch(Harmony h)
        {
            var t = typeof(Renderer);
            var m = AccessTools.PropertySetter(t, nameof(Renderer.materials));
            var mw = new HarmonyMethod(typeof(RendererMaterialChangePre).GetMethod(
                nameof(prefixOnMaterialArraySet),
                BindingFlags.Static | BindingFlags.NonPublic
            ));

            h.Patch(original: m, prefix: mw);
        }

        private static void prefixOnMaterialArraySet(Renderer __instance, Material[] __0)
        {
            var pass = PropertyChangeTracker.GetInnermostPass();
            if (pass == null) return;
            
            var old = __instance.materials;
            
            var installed = __0.Except(old).ToList();
            if (installed.Count != 0)
            {
                foreach (var ins in installed)
                {
                    Debug.Log($"Renderer: {ins} was added to {__instance} by {pass}");
                }
            }
            
            var removed = old.Except(__0).ToList();
            if (removed.Count != 0)
            {
                foreach (var remove in removed)
                {
                    Debug.Log($"Renderer: {remove} was removed from {__instance} by {pass}");
                }
            }
        }
    }

    /// <summary>
    /// Tracks <see cref="GameObject.AddComponent(Type)" /> and <see cref="GameObject.AddComponent{T}"/>.
    /// </summary>
    internal static class AddComponentPre 
    {
        internal static void Patch(Harmony h)
        {
            var t = typeof(GameObject);
            {
                var m = AccessTools.Method(t, nameof(GameObject.AddComponent), new[] { typeof(Type) });
                var mw = new HarmonyMethod(typeof(AddComponentPre).GetMethod(nameof(addComponentPre), BindingFlags.Static | BindingFlags.NonPublic));
                
                h.Patch(original: m, prefix: mw);
            }
        }

        private static void addComponentPre(GameObject __instance, Type componentType)
        {
            var t = PropertyChangeTracker.GetInnermostPass();
            if (t != null)
            {
                Debug.Log($"GameObject: {__instance} was added {componentType} by {t}");
            }
        }
    }

    /// <summary>
    /// Tracks <see cref="Material"/> properties change. This includes:
    /// <list type="bullet">
    /// <item>
    /// <see cref="Material.SetFloat(int, float)"/>
    /// </item>
    /// <item>
    /// <see cref="Material.SetFloat(string, float)"/>
    /// </item>
    /// <item>
    /// <see cref="Material.SetInt(int, int)"/>
    /// </item>
    /// <item>
    /// <see cref="Material.SetInt(string, int)"/>
    /// </item>
    /// <item>
    /// <see cref="Material.SetColorArray"/>
    /// </item>
    /// <item>
    /// <see cref="Material.SetVectorArray"/>
    /// </item>
    /// <item>
    /// <see cref="Material.SetMatrixArray"/>
    /// </item>
    /// <item>
    /// <see cref="Material.SetTextureOffset"/>
    /// </item>
    /// <item>
    /// <see cref="Material.SetTextureScale"/>
    /// </item>
    /// <item>
    /// <see cref="Material.SetTextureOffset"/>
    /// </item>
    /// </list>
    /// </summary>
    internal static class ShaderPropertyModificationPre
    {
        // TODO: shader property modification? - Maybe useful for texturing tools (i.e. TexTransTool)
    }
#endif
}
