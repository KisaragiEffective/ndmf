﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.build_framework.model;
using nadena.dev.build_framework.reporting;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace nadena.dev.build_framework.ui
{
    public class SolverWindow : EditorWindow
    {
        [MenuItem("Window/[ABPF] Plugin sequence display")]
        public static void ShowWindow()
        {
            GetWindow<SolverWindow>("Plugin sequence display");
        }
        
        private SolverUI _solverUI;
        
        private void OnEnable()
        {
            _solverUI = new SolverUI();
            BuildEvent.OnBuildEvent += OnBuildEvent;
        }

        private void OnDisable()
        {
            BuildEvent.OnBuildEvent -= OnBuildEvent;
        }

        private void OnBuildEvent(BuildEvent ev)
        {
            if (ev is BuildEvent.BuildEnded)
            {
                _solverUI.Reload();
            }
        }

        void OnGUI()
        {
            if (_solverUI != null)
            {
                _solverUI.OnGUI(new UnityEngine.Rect(0, 0, position.width, position.height));
            }
        }
    }

    internal class SolverUIItem : TreeViewItem
    {
        public double? ExecutionTimeMS;
    }
    
    public class SolverUI : TreeView
    {
        private static PluginResolver Resolver = new PluginResolver();
        
        public SolverUI() : this(new TreeViewState())
        {
        }
        
        public SolverUI(TreeViewState state) : base(state)
        {
            Reload();
        }

        BuildEvent.PassExecuted NextPassExecuted(IEnumerator<BuildEvent> events)
        {
            while (events.MoveNext())
            {
                if (events.Current is BuildEvent.PassExecuted pe) return pe;
            }

            return null;
        }
        
        protected override TreeViewItem BuildRoot()
        {
            
            var root = new SolverUIItem() {id = 0, depth = -1, displayName = "Avatar Build"};
            var allItems = new List<SolverUIItem>();
            
            int id = 1;

            IEnumerator<BuildEvent> events = BuildEvent.LastBuildEvents.GetEnumerator();

            foreach (var phaseKVP in Resolver.Passes)
            {
                var phase = phaseKVP.Key;
                var passes = phaseKVP.Value;
                InstantiatedPlugin priorPlugin = null;
                SolverUIItem pluginItem = null;

                allItems.Add(new SolverUIItem() {id = id++, depth = 1, displayName = phase.ToString()});
                var phaseItem = allItems[allItems.Count - 1];

                foreach (var pass in passes)
                {
                    if (pass.InstantiatedPass.InternalPass) continue;

                    var plugin = pass.InstantiatedPass.Plugin;
                    if (plugin != priorPlugin)
                    {
                        allItems.Add(new SolverUIItem() {id = id++, depth = 2, displayName = plugin.Description});
                        priorPlugin = plugin;
                        pluginItem = allItems[allItems.Count - 1];
                    }

                    allItems.Add(new SolverUIItem() {id = id++, depth = 3, displayName = pass.Description});
                    BuildEvent.PassExecuted passEvent;

                    do
                    {
                        passEvent = NextPassExecuted(events);
                    } while (passEvent != null && passEvent.QualifiedName != pass.InstantiatedPass.QualifiedName);

                    var passItem = allItems[allItems.Count - 1];
                    if (passEvent == null) continue;
                    
                    passItem.ExecutionTimeMS = passEvent.PassExecutionTime;

                    if (passEvent.PassActivationTimes.Count > 0 || passEvent.PassDeactivationTimes.Count > 0)
                    {
                        passItem.ExecutionTimeMS = passEvent.PassExecutionTime;

                        foreach (var kvp in passEvent.PassDeactivationTimes)
                        {
                            var ty = kvp.Key;
                            var time = kvp.Value;

                            allItems.Add(new SolverUIItem()
                                {id = id++, depth = 4, displayName = $"Deactivate {ty.Name}", ExecutionTimeMS = time});
                            passItem.ExecutionTimeMS += time;
                        }

                        foreach (var kvp in passEvent.PassActivationTimes)
                        {
                            var ty = kvp.Key;
                            var time = kvp.Value;

                            allItems.Add(new SolverUIItem()
                                {id = id++, depth = 4, displayName = $"Activate {ty.Name}", ExecutionTimeMS = time});
                            passItem.ExecutionTimeMS += time;
                        }

                        allItems.Add(new SolverUIItem()
                        {
                            id = id++, depth = 4, displayName = "Pass execution",
                            ExecutionTimeMS = passEvent.PassExecutionTime
                        });
                    }

                    if (pluginItem.ExecutionTimeMS == null) pluginItem.ExecutionTimeMS = 0;
                    if (phaseItem.ExecutionTimeMS == null) phaseItem.ExecutionTimeMS = 0;
                    pluginItem.ExecutionTimeMS += passItem.ExecutionTimeMS;
                    phaseItem.ExecutionTimeMS += passItem.ExecutionTimeMS;
                }

            }
            foreach (var pass in allItems)
            {
                if (pass.ExecutionTimeMS.HasValue)
                {
                    pass.displayName = $"({pass.ExecutionTimeMS:F}ms) {pass.displayName}";
                }
            }

            SetupParentsAndChildrenFromDepths(root, allItems.Select(i => (TreeViewItem)i).ToList());

            return root;
        }
    }
}