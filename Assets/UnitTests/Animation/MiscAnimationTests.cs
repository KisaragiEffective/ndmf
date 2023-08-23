﻿using nadena.dev.build_framework;
using nadena.dev.build_framework.animation;
using NUnit.Framework;
using UnityEditor;

namespace UnitTests
{
    public class MiscAnimationTests : TestBase
    {
        [Test]
        public void HighQualityCurvesSettingPreserved()
        {
            var prefab = CreatePrefab("HighQualityCurvesSettingPreserved.prefab");
            var context = new BuildContext(prefab, null);
            context.ActivateExtensionContext<TrackObjectRenamesContext>();
            context.DeactivateExtensionContext<TrackObjectRenamesContext>();

            var layer = findFxLayer(prefab, "Base Layer");

            var hq_on = FindStateInLayer(layer, "HQ_ON");
            var hq_off = FindStateInLayer(layer, "HQ_OFF");

            Assert.True(new SerializedObject(hq_on.motion).FindProperty("m_UseHighQualityCurve").boolValue);
            Assert.False(new SerializedObject(hq_off.motion).FindProperty("m_UseHighQualityCurve").boolValue);
        }
    }
}