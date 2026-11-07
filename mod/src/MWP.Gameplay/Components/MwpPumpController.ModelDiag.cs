using System;
using System.Collections.Generic;
using System.Text;
using Timberborn.Hauling;
using UnityEngine;

namespace MWP.Gameplay.Components
{
    /// <summary>
    /// TEMPORARY in-game diagnostic (2026-11-07): Mk III/IV were reported to have an empty interior
    /// while Mk I/II look correct. This logs the instantiated prefab hierarchy (node names, local
    /// positions, mesh vertex counts and renderer state) once per pump so Player.log can tell
    /// whether the animated child nodes exist in the game and whether their renderers/meshes are
    /// present. Remove together with the call in <see cref="Activate"/> after the investigation.
    /// </summary>
    public sealed partial class MwpPumpController
    {
        private bool _modelHierarchyLogged;

        private void LogModelHierarchyOnce()
        {
            if (_modelHierarchyLogged)
            {
                return;
            }

            _modelHierarchyLogged = true;
            try
            {
                var builder = new StringBuilder(768);
                builder.Append("model diag tier=").Append(_tierName)
                    .Append(" entity=").Append(_entityId.ToString("N"))
                    .Append(" root=").Append(GameObject.name)
                    .Append(" children=").Append(GameObject.transform.childCount);
                AppendModelChildren(builder, GameObject.transform, 0);
                AppendHaulingState(builder);
                _runtime.RawLog.Info("MWP/Info/ModelDiag/" + _entityId.ToString("N"), builder.ToString());
            }
            catch (Exception)
            {
                // Diagnostics must never break the pump.
            }
        }

        private void AppendHaulingState(StringBuilder builder)
        {
            var providers = new List<IHaulBehaviorProvider>();
            GetComponents(providers);
            builder.Append("\nhauling: HaulCandidate=").Append(GetComponent<HaulCandidate>() != null ? "yes" : "no")
                .Append(" providers=").Append(providers.Count);
            foreach (IHaulBehaviorProvider provider in providers)
            {
                builder.Append(' ').Append(provider.GetType().Name);
            }
        }

        private static void AppendModelChildren(StringBuilder builder, Transform parent, int depth)
        {
            if (depth >= 6)
            {
                return;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                MeshFilter filter = child.GetComponent<MeshFilter>();
                Renderer renderer = child.GetComponent<Renderer>();
                int vertices = filter != null && filter.sharedMesh != null ? filter.sharedMesh.vertexCount : 0;
                builder.Append('\n');
                for (int pad = 0; pad <= depth; pad++)
                {
                    builder.Append("  ");
                }

                builder.Append(child.name)
                    .Append(" pos=").Append(child.localPosition.ToString("F2"))
                    .Append(" verts=").Append(vertices)
                    .Append(" renderer=").Append(renderer != null ? (renderer.enabled ? "on" : "off") : "none");
                AppendModelChildren(builder, child, depth + 1);
            }
        }
    }
}
