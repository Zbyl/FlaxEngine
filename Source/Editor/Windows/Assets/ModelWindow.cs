// Copyright (c) 2012-2021 Wojciech Figat. All rights reserved.

using System.Collections.Generic;
using System.Reflection;
using FlaxEditor.Content;
using FlaxEditor.Content.Import;
using FlaxEditor.CustomEditors;
using FlaxEditor.GUI;
using FlaxEditor.Scripting;
using FlaxEditor.Viewport.Cameras;
using FlaxEditor.Viewport.Previews;
using FlaxEngine;
using FlaxEngine.GUI;
using FlaxEngine.Utilities;
using Object = FlaxEngine.Object;

namespace FlaxEditor.Windows.Assets
{
    /// <summary>
    /// Editor window to view/modify <see cref="Model"/> asset.
    /// </summary>
    /// <seealso cref="Model" />
    /// <seealso cref="FlaxEditor.Windows.Assets.AssetEditorWindow" />
    public sealed class ModelWindow : ModelBaseWindow<Model, ModelWindow>
    {
        private sealed class Preview : ModelPreview
        {
            private readonly ModelWindow _window;

            public Preview(ModelWindow window)
            : base(true)
            {
                _window = window;

                // Enable shadows
                PreviewLight.ShadowsMode = ShadowsCastingMode.All;
                PreviewLight.CascadeCount = 3;
                PreviewLight.ShadowsDistance = 2000.0f;
                Task.ViewFlags |= ViewFlags.Shadows;
            }
            
            public override void Draw()
            {
                base.Draw();

                var style = Style.Current;
                var asset = _window.Asset;
                if (asset == null || !asset.IsLoaded)
                {
                    Render2D.DrawText(style.FontLarge, "Loading...", new Rectangle(Vector2.Zero, Size), style.ForegroundDisabled, TextAlignment.Center, TextAlignment.Center);
                }
            }
        }

        [CustomEditor(typeof(ProxyEditor))]
        private sealed class MeshesPropertiesProxy : PropertiesProxyBase
        {
            private readonly List<ComboBox> _materialSlotComboBoxes = new List<ComboBox>();
            private readonly List<CheckBox> _isolateCheckBoxes = new List<CheckBox>();
            private readonly List<CheckBox> _highlightCheckBoxes = new List<CheckBox>();

            public override void OnLoad(ModelWindow window)
            {
                base.OnLoad(window);

                Window._isolateIndex = -1;
                Window._highlightIndex = -1;
            }

            public override void OnClean()
            {
                Window._isolateIndex = -1;
                Window._highlightIndex = -1;

                base.OnClean();
            }

            /// <summary>
            /// Updates the highlight/isolate effects on UI.
            /// </summary>
            public void UpdateEffectsOnUI()
            {
                Window._skipEffectsGuiEvents = true;

                for (int i = 0; i < _isolateCheckBoxes.Count; i++)
                {
                    var checkBox = _isolateCheckBoxes[i];
                    checkBox.Checked = Window._isolateIndex == ((Mesh)checkBox.Tag).MaterialSlotIndex;
                }

                for (int i = 0; i < _highlightCheckBoxes.Count; i++)
                {
                    var checkBox = _highlightCheckBoxes[i];
                    checkBox.Checked = Window._highlightIndex == ((Mesh)checkBox.Tag).MaterialSlotIndex;
                }

                Window._skipEffectsGuiEvents = false;
            }

            /// <summary>
            /// Updates the material slots UI parts. Should be called after material slot rename.
            /// </summary>
            public void UpdateMaterialSlotsUI()
            {
                Window._skipEffectsGuiEvents = true;

                // Generate material slots labels (with index prefix)
                var slots = Asset.MaterialSlots;
                var slotsLabels = new string[slots.Length];
                for (int i = 0; i < slots.Length; i++)
                {
                    slotsLabels[i] = string.Format("[{0}] {1}", i, slots[i].Name);
                }

                // Update comboboxes
                for (int i = 0; i < _materialSlotComboBoxes.Count; i++)
                {
                    var comboBox = _materialSlotComboBoxes[i];
                    comboBox.SetItems(slotsLabels);
                    comboBox.SelectedIndex = ((Mesh)comboBox.Tag).MaterialSlotIndex;
                }

                Window._skipEffectsGuiEvents = false;
            }

            /// <summary>
            /// Sets the material slot index to the mesh.
            /// </summary>
            /// <param name="mesh">The mesh.</param>
            /// <param name="newSlotIndex">New index of the material slot to use.</param>
            public void SetMaterialSlot(Mesh mesh, int newSlotIndex)
            {
                if (Window._skipEffectsGuiEvents)
                    return;

                mesh.MaterialSlotIndex = newSlotIndex == -1 ? 0 : newSlotIndex;
                Window.UpdateEffectsOnAsset();
                UpdateEffectsOnUI();
                Window.MarkAsEdited();
            }

            /// <summary>
            /// Sets the material slot to isolate.
            /// </summary>
            /// <param name="mesh">The mesh.</param>
            public void SetIsolate(Mesh mesh)
            {
                if (Window._skipEffectsGuiEvents)
                    return;

                Window._isolateIndex = mesh?.MaterialSlotIndex ?? -1;
                Window.UpdateEffectsOnAsset();
                UpdateEffectsOnUI();
            }

            /// <summary>
            /// Sets the material slot index to highlight.
            /// </summary>
            /// <param name="mesh">The mesh.</param>
            public void SetHighlight(Mesh mesh)
            {
                if (Window._skipEffectsGuiEvents)
                    return;

                Window._highlightIndex = mesh?.MaterialSlotIndex ?? -1;
                Window.UpdateEffectsOnAsset();
                UpdateEffectsOnUI();
            }

            private class ProxyEditor : ProxyEditorBase
            {
                public override void Initialize(LayoutElementsContainer layout)
                {
                    var proxy = (MeshesPropertiesProxy)Values[0];
                    if (proxy.Asset == null || !proxy.Asset.IsLoaded)
                    {
                        layout.Label("Loading...");
                        return;
                    }
                    proxy._materialSlotComboBoxes.Clear();
                    proxy._isolateCheckBoxes.Clear();
                    proxy._highlightCheckBoxes.Clear();
                    var lods = proxy.Asset.LODs;
                    var loadedLODs = proxy.Asset.LoadedLODs;

                    // General properties
                    {
                        var group = layout.Group("General");

                        var minScreenSize = group.FloatValue("Min Screen Size", "The minimum screen size to draw model (the bottom limit). Used to cull small models. Set to 0 to disable this feature.");
                        minScreenSize.FloatValue.MinValue = 0.0f;
                        minScreenSize.FloatValue.MaxValue = 1.0f;
                        minScreenSize.FloatValue.Value = proxy.Asset.MinScreenSize;
                        minScreenSize.FloatValue.ValueChanged += () =>
                        {
                            proxy.Asset.MinScreenSize = minScreenSize.FloatValue.Value;
                            proxy.Window.MarkAsEdited();
                        };
                    }

                    // Group per LOD
                    for (int lodIndex = 0; lodIndex < lods.Length; lodIndex++)
                    {
                        var group = layout.Group("LOD " + lodIndex);
                        if (lodIndex < lods.Length - loadedLODs)
                        {
                            group.Label("Loading LOD...");
                            continue;
                        }
                        var lod = lods[lodIndex];
                        var meshes = lod.Meshes;

                        int triangleCount = 0, vertexCount = 0;
                        for (int meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
                        {
                            var mesh = meshes[meshIndex];
                            triangleCount += mesh.TriangleCount;
                            vertexCount += mesh.VertexCount;
                        }

                        group.Label(string.Format("Triangles: {0:N0}   Vertices: {1:N0}", triangleCount, vertexCount));
                        group.Label("Size: " + lod.Box.Size);
                        var screenSize = group.FloatValue("Screen Size", "The screen size to switch LODs. Bottom limit of the model screen size to render this LOD.");
                        screenSize.FloatValue.MinValue = 0.0f;
                        screenSize.FloatValue.MaxValue = 10.0f;
                        screenSize.FloatValue.Value = lod.ScreenSize;
                        screenSize.FloatValue.ValueChanged += () =>
                        {
                            lod.ScreenSize = screenSize.FloatValue.Value;
                            proxy.Window.MarkAsEdited();
                        };

                        // Every mesh properties
                        for (int meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
                        {
                            var mesh = meshes[meshIndex];
                            group.Label($"Mesh {meshIndex} (tris: {mesh.TriangleCount:N0}, verts: {mesh.VertexCount:N0})");

                            // Material Slot
                            var materialSlot = group.ComboBox("Material Slot", "Material slot used by this mesh during rendering");
                            materialSlot.ComboBox.Tag = mesh;
                            materialSlot.ComboBox.SelectedIndexChanged += comboBox => proxy.SetMaterialSlot((Mesh)comboBox.Tag, comboBox.SelectedIndex);
                            proxy._materialSlotComboBoxes.Add(materialSlot.ComboBox);

                            // Isolate
                            var isolate = group.Checkbox("Isolate", "Shows only this mesh (and meshes using the same material slot)");
                            isolate.CheckBox.Tag = mesh;
                            isolate.CheckBox.StateChanged += (box) => proxy.SetIsolate(box.Checked ? (Mesh)box.Tag : null);
                            proxy._isolateCheckBoxes.Add(isolate.CheckBox);

                            // Highlight
                            var highlight = group.Checkbox("Highlight", "Highlights this mesh with a tint color (and meshes using the same material slot)");
                            highlight.CheckBox.Tag = mesh;
                            highlight.CheckBox.StateChanged += (box) => proxy.SetHighlight(box.Checked ? (Mesh)box.Tag : null);
                            proxy._highlightCheckBoxes.Add(highlight.CheckBox);
                        }
                    }

                    // Refresh UI
                    proxy.UpdateMaterialSlotsUI();
                }

                internal override void RefreshInternal()
                {
                    // Skip updates when model is not loaded
                    var proxy = (MeshesPropertiesProxy)Values[0];
                    if (proxy.Asset == null || !proxy.Asset.IsLoaded)
                        return;

                    base.RefreshInternal();
                }
            }
        }

        [CustomEditor(typeof(ProxyEditor))]
        private sealed class MaterialsPropertiesProxy : PropertiesProxyBase
        {
            [Collection(CanReorderItems = true, NotNullItems = true, OverrideEditorTypeName = "FlaxEditor.CustomEditors.Editors.GenericEditor", Spacing = 10)]
            [EditorOrder(10), EditorDisplay("Materials", EditorDisplayAttribute.InlineStyle)]
            public MaterialSlot[] MaterialSlots
            {
                get => Asset?.MaterialSlots;
                set
                {
                    if (Asset != null)
                    {
                        if (Asset.MaterialSlots.Length != value.Length)
                        {
                            MaterialBase[] materials = new MaterialBase[value.Length];
                            string[] names = new string[value.Length];
                            ShadowsCastingMode[] shadowsModes = new ShadowsCastingMode[value.Length];
                            for (int i = 0; i < value.Length; i++)
                            {
                                if (value[i] != null)
                                {
                                    materials[i] = value[i].Material;
                                    names[i] = value[i].Name;
                                    shadowsModes[i] = value[i].ShadowsMode;
                                }
                                else
                                {
                                    materials[i] = null;
                                    names[i] = "Material " + i;
                                    shadowsModes[i] = ShadowsCastingMode.All;
                                }
                            }

                            Asset.SetupMaterialSlots(value.Length);

                            var slots = Asset.MaterialSlots;
                            for (int i = 0; i < slots.Length; i++)
                            {
                                slots[i].Material = materials[i];
                                slots[i].Name = names[i];
                                slots[i].ShadowsMode = shadowsModes[i];
                            }

                            UpdateMaterialSlotsUI();
                        }
                    }
                }
            }

            private readonly List<ComboBox> _materialSlotComboBoxes = new List<ComboBox>();

            /// <summary>
            /// Updates the material slots UI parts. Should be called after material slot rename.
            /// </summary>
            public void UpdateMaterialSlotsUI()
            {
                Window._skipEffectsGuiEvents = true;

                // Generate material slots labels (with index prefix)
                var slots = Asset.MaterialSlots;
                var slotsLabels = new string[slots.Length];
                for (int i = 0; i < slots.Length; i++)
                {
                    slotsLabels[i] = string.Format("[{0}] {1}", i, slots[i].Name);
                }

                // Update comboboxes
                for (int i = 0; i < _materialSlotComboBoxes.Count; i++)
                {
                    var comboBox = _materialSlotComboBoxes[i];
                    comboBox.SetItems(slotsLabels);
                    comboBox.SelectedIndex = ((Mesh)comboBox.Tag).MaterialSlotIndex;
                }

                Window._skipEffectsGuiEvents = false;
            }

            private class ProxyEditor : ProxyEditorBase
            {
                public override void Initialize(LayoutElementsContainer layout)
                {
                    var proxy = (MaterialsPropertiesProxy)Values[0];
                    if (proxy.Asset == null || !proxy.Asset.IsLoaded)
                    {
                        layout.Label("Loading...");
                        return;
                    }

                    base.Initialize(layout);
                }
            }
        }

        [CustomEditor(typeof(ProxyEditor))]
        private sealed class UVsPropertiesProxy : PropertiesProxyBase
        {
            public enum UVChannel
            {
                None,
                TexCoord,
                LightmapUVs,
            };

            private UVChannel _uvChannel = UVChannel.None;

            [EditorOrder(0), EditorDisplay(null, "Preview UV Channel"), EnumDisplay(EnumDisplayAttribute.FormatMode.None)]
            [Tooltip("Set UV channel to preview.")]
            public UVChannel Channel
            {
                get => _uvChannel;
                set
                {
                    if (_uvChannel == value)
                        return;
                    _uvChannel = value;
                    Window._meshData?.RequestMeshData(Window._asset);
                }
            }

            [EditorOrder(1), EditorDisplay(null, "LOD"), Limit(0, Model.MaxLODs), VisibleIf("ShowUVs")]
            [Tooltip("Level Of Detail index to preview UVs layout.")]
            public int LOD = 0;

            [EditorOrder(2), EditorDisplay(null, "Mesh"), Limit(-1, 1000000), VisibleIf("ShowUVs")]
            [Tooltip("Mesh index to preview UVs layout. Use -1 for all meshes")]
            public int Mesh = -1;

            private bool ShowUVs => _uvChannel != UVChannel.None;

            /// <inheritdoc />
            public override void OnClean()
            {
                Channel = UVChannel.None;

                base.OnClean();
            }

            private class ProxyEditor : ProxyEditorBase
            {
                private UVsLayoutPreviewControl _uvsPreview;

                public override void Initialize(LayoutElementsContainer layout)
                {
                    var proxy = (UVsPropertiesProxy)Values[0];
                    if (proxy.Asset == null || !proxy.Asset.IsLoaded)
                    {
                        layout.Label("Loading...");
                        return;
                    }

                    base.Initialize(layout);

                    _uvsPreview = layout.Custom<UVsLayoutPreviewControl>().CustomControl;
                    _uvsPreview.Proxy = proxy;
                }

                /// <inheritdoc />
                public override void Refresh()
                {
                    base.Refresh();

                    if (_uvsPreview != null)
                    {
                        _uvsPreview.Channel = _uvsPreview.Proxy._uvChannel;
                        _uvsPreview.LOD = _uvsPreview.Proxy.LOD;
                        _uvsPreview.Mesh = _uvsPreview.Proxy.Mesh;
                        _uvsPreview.HighlightIndex = _uvsPreview.Proxy.Window._highlightIndex;
                        _uvsPreview.IsolateIndex = _uvsPreview.Proxy.Window._isolateIndex;
                    }
                }

                protected override void Deinitialize()
                {
                    _uvsPreview = null;

                    base.Deinitialize();
                }
            }

            private sealed class UVsLayoutPreviewControl : RenderToTextureControl
            {
                private UVChannel _channel;
                private int _lod, _mesh = -1;
                private int _highlightIndex = -1;
                private int _isolateIndex = -1;
                public UVsPropertiesProxy Proxy;

                public UVsLayoutPreviewControl()
                {
                    Offsets = new Margin(4);
                    AutomaticInvalidate = false;
                }

                public UVChannel Channel
                {
                    set
                    {
                        if (_channel == value)
                            return;
                        _channel = value;
                        Visible = _channel != UVChannel.None;
                        if (Visible)
                            Invalidate();
                    }
                }

                public int LOD
                {
                    set
                    {
                        if (_lod != value)
                        {
                            _lod = value;
                            Invalidate();
                        }
                    }
                }

                public int Mesh
                {
                    set
                    {
                        if (_mesh != value)
                        {
                            _mesh = value;
                            Invalidate();
                        }
                    }
                }

                public int HighlightIndex
                {
                    set
                    {
                        if (_highlightIndex != value)
                        {
                            _highlightIndex = value;
                            Invalidate();
                        }
                    }
                }

                public int IsolateIndex
                {
                    set
                    {
                        if (_isolateIndex != value)
                        {
                            _isolateIndex = value;
                            Invalidate();
                        }
                    }
                }

                private void DrawMeshUVs(int meshIndex, MeshDataCache.MeshData meshData)
                {
                    var uvScale = Size;
                    if (meshData.IndexBuffer == null || meshData.VertexBuffer == null)
                        return;
                    var linesColor = _highlightIndex != -1 && _highlightIndex == meshIndex ? Style.Current.BackgroundSelected : Color.White;
                    switch (_channel)
                    {
                    case UVChannel.TexCoord:
                        for (int i = 0; i < meshData.IndexBuffer.Length; i += 3)
                        {
                            // Cache triangle indices
                            int i0 = meshData.IndexBuffer[i + 0];
                            int i1 = meshData.IndexBuffer[i + 1];
                            int i2 = meshData.IndexBuffer[i + 2];

                            // Cache triangle uvs positions and transform positions to output target
                            Vector2 uv0 = meshData.VertexBuffer[i0].TexCoord * uvScale;
                            Vector2 uv1 = meshData.VertexBuffer[i1].TexCoord * uvScale;
                            Vector2 uv2 = meshData.VertexBuffer[i2].TexCoord * uvScale;

                            // Don't draw too small triangles
                            float area = Vector2.TriangleArea(ref uv0, ref uv1, ref uv2);
                            if (area > 10.0f)
                            {
                                // Draw triangle
                                Render2D.DrawLine(uv0, uv1, linesColor);
                                Render2D.DrawLine(uv1, uv2, linesColor);
                                Render2D.DrawLine(uv2, uv0, linesColor);
                            }
                        }
                        break;
                    case UVChannel.LightmapUVs:
                        for (int i = 0; i < meshData.IndexBuffer.Length; i += 3)
                        {
                            // Cache triangle indices
                            int i0 = meshData.IndexBuffer[i + 0];
                            int i1 = meshData.IndexBuffer[i + 1];
                            int i2 = meshData.IndexBuffer[i + 2];

                            // Cache triangle uvs positions and transform positions to output target
                            Vector2 uv0 = meshData.VertexBuffer[i0].LightmapUVs * uvScale;
                            Vector2 uv1 = meshData.VertexBuffer[i1].LightmapUVs * uvScale;
                            Vector2 uv2 = meshData.VertexBuffer[i2].LightmapUVs * uvScale;

                            // Don't draw too small triangles
                            float area = Vector2.TriangleArea(ref uv0, ref uv1, ref uv2);
                            if (area > 3.0f)
                            {
                                // Draw triangle
                                Render2D.DrawLine(uv0, uv1, linesColor);
                                Render2D.DrawLine(uv1, uv2, linesColor);
                                Render2D.DrawLine(uv2, uv0, linesColor);
                            }
                        }
                        break;
                    }
                }

                /// <inheritdoc />
                public override void DrawSelf()
                {
                    base.DrawSelf();

                    var size = Size;
                    if (_channel == UVChannel.None || size.MaxValue < 5.0f)
                        return;
                    if (Proxy.Window._meshData == null)
                        Proxy.Window._meshData = new MeshDataCache();
                    if (!Proxy.Window._meshData.RequestMeshData(Proxy.Window._asset))
                    {
                        Invalidate();
                        Render2D.DrawText(Style.Current.FontMedium, "Loading...", new Rectangle(Vector2.Zero, size), Color.White, TextAlignment.Center, TextAlignment.Center);
                        return;
                    }

                    Render2D.PushClip(new Rectangle(Vector2.Zero, size));

                    var meshDatas = Proxy.Window._meshData.MeshDatas;
                    var lodIndex = Mathf.Clamp(_lod, 0, meshDatas.Length - 1);
                    var lod = meshDatas[lodIndex];
                    var mesh = Mathf.Clamp(_mesh, -1, lod.Length - 1);
                    if (mesh == -1)
                    {
                        for (int meshIndex = 0; meshIndex < lod.Length; meshIndex++)
                        {
                            if (_isolateIndex != -1 && _isolateIndex != meshIndex)
                                continue;
                            DrawMeshUVs(meshIndex, lod[meshIndex]);
                        }
                    }
                    else
                    {
                        DrawMeshUVs(mesh, lod[mesh]);
                    }

                    Render2D.PopClip();
                }

                protected override void OnSizeChanged()
                {
                    Height = Width;

                    base.OnSizeChanged();
                }

                protected override void OnVisibleChanged()
                {
                    base.OnVisibleChanged();

                    Parent.PerformLayout();
                    Height = Width;
                }
            }
        }

        [CustomEditor(typeof(ProxyEditor))]
        private sealed class ImportPropertiesProxy : PropertiesProxyBase
        {
            private ModelImportSettings ImportSettings = new ModelImportSettings();

            /// <inheritdoc />
            public override void OnLoad(ModelWindow window)
            {
                base.OnLoad(window);

                ModelImportSettings.TryRestore(ref ImportSettings, window.Item.Path);
            }

            public void Reimport()
            {
                Editor.Instance.ContentImporting.Reimport((BinaryAssetItem)Window.Item, ImportSettings, true);
            }

            private class ProxyEditor : ProxyEditorBase
            {
                public override void Initialize(LayoutElementsContainer layout)
                {
                    var proxy = (ImportPropertiesProxy)Values[0];
                    if (proxy.Asset == null || !proxy.Asset.IsLoaded)
                    {
                        layout.Label("Loading...");
                        return;
                    }

                    // Import Settings
                    {
                        var group = layout.Group("Import Settings");

                        var importSettingsField = typeof(ImportPropertiesProxy).GetField("ImportSettings", BindingFlags.NonPublic | BindingFlags.Instance);
                        var importSettingsValues = new ValueContainer(new ScriptMemberInfo(importSettingsField)) { proxy.ImportSettings };
                        group.Object(importSettingsValues);

                        layout.Space(5);
                        var reimportButton = group.Button("Reimport");
                        reimportButton.Button.Clicked += () => ((ImportPropertiesProxy)Values[0]).Reimport();
                    }
                }
            }
        }

        private class MeshesTab : Tab
        {
            public MeshesTab(ModelWindow window)
            : base("Meshes", window)
            {
                Proxy = new MeshesPropertiesProxy();
                Presenter.Select(Proxy);
            }
        }

        private class MaterialsTab : Tab
        {
            public MaterialsTab(ModelWindow window)
            : base("Materials", window)
            {
                Proxy = new MaterialsPropertiesProxy();
                Presenter.Select(Proxy);
            }
        }

        private class UVsTab : Tab
        {
            public UVsTab(ModelWindow window)
            : base("UVs", window, false)
            {
                Proxy = new UVsPropertiesProxy();
                Presenter.Select(Proxy);
            }
        }

        private class ImportTab : Tab
        {
            public ImportTab(ModelWindow window)
            : base("Import", window, false)
            {
                Proxy = new ImportPropertiesProxy();
                Presenter.Select(Proxy);
            }
        }

        private readonly ModelPreview _preview;
        private StaticModel _highlightActor;
        private MeshDataCache _meshData;

        /// <inheritdoc />
        public ModelWindow(Editor editor, AssetItem item)
        : base(editor, item)
        {
            // Toolstrip
            _toolstrip.AddSeparator();
            _toolstrip.AddButton(editor.Icons.Docs64, () => Platform.OpenUrl(Utilities.Constants.DocsUrl + "manual/graphics/models/index.html")).LinkTooltip("See documentation to learn more");

            // Model preview
            _preview = new Preview(this)
            {
                ViewportCamera = new FPSCamera(),
                ScaleToFit = false,
                Parent = _split.Panel1
            };

            // Properties tabs
            _tabs.AddTab(new MeshesTab(this));
            _tabs.AddTab(new MaterialsTab(this));
            _tabs.AddTab(new UVsTab(this));
            _tabs.AddTab(new ImportTab(this));

            // Highlight actor (used to highlight selected material slot, see UpdateEffectsOnAsset)
            _highlightActor = new StaticModel
            {
                IsActive = false
            };
            _preview.Task.AddCustomActor(_highlightActor);
        }

        /// <summary>
        /// Updates the highlight/isolate effects on a model asset.
        /// </summary>
        private void UpdateEffectsOnAsset()
        {
            var entries = _preview.PreviewActor.Entries;
            if (entries != null)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    entries[i].Visible = _isolateIndex == -1 || _isolateIndex == i;
                }
                _preview.PreviewActor.Entries = entries;
            }

            if (_highlightIndex != -1)
            {
                _highlightActor.IsActive = true;

                var highlightMaterial = EditorAssets.Cache.HighlightMaterialInstance;
                entries = _highlightActor.Entries;
                if (entries != null)
                {
                    for (int i = 0; i < entries.Length; i++)
                    {
                        entries[i].Material = highlightMaterial;
                        entries[i].Visible = _highlightIndex == i;
                    }
                    _highlightActor.Entries = entries;
                }
            }
            else
            {
                _highlightActor.IsActive = false;
            }
        }

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            // Sync highlight actor size with actual preview model (preview scales model for better usage experience)
            if (_highlightActor && _highlightActor.IsActive)
            {
                _highlightActor.Transform = _preview.PreviewActor.Transform;
            }

            // Model is loaded but LODs data may be during streaming so refresh properties on fully loaded
            if (_refreshOnLODsLoaded && _asset && _asset.LoadedLODs == _asset.LODs.Length)
            {
                _refreshOnLODsLoaded = false;
                foreach (var child in _tabs.Children)
                {
                    if (child is Tab tab)
                    {
                        tab.Presenter.BuildLayout();
                    }
                }
            }

            base.Update(deltaTime);
        }

        /// <inheritdoc />
        public override void Save()
        {
            if (!IsEdited)
                return;

            if (_asset.WaitForLoaded())
            {
                return;
            }

            if (_asset.Save())
            {
                Editor.LogError("Cannot save asset.");
                return;
            }

            ClearEditedFlag();
            _item.RefreshThumbnail();
        }

        /// <inheritdoc />
        protected override void UnlinkItem()
        {
            _meshData?.WaitForMeshDataRequestEnd();
            _preview.Model = null;
            _highlightActor.Model = null;

            base.UnlinkItem();
        }

        /// <inheritdoc />
        protected override void OnAssetLinked()
        {
            _preview.Model = _asset;
            _highlightActor.Model = _asset;

            base.OnAssetLinked();
        }

        /// <inheritdoc />
        protected override void OnAssetLoaded()
        {
            _refreshOnLODsLoaded = true;
            _preview.ViewportCamera.SetArcBallView(Asset.GetBox());
            UpdateEffectsOnAsset();

            // TODO: disable streaming for this model

            base.OnAssetLoaded();
        }

        /// <inheritdoc />
        public override void OnItemReimported(ContentItem item)
        {
            // Discard any old mesh data cache
            _meshData?.Dispose();

            base.OnItemReimported(item);
        }

        /// <inheritdoc />
        public override void OnDestroy()
        {
            _meshData?.Dispose();
            _meshData = null;

            base.OnDestroy();

            Object.Destroy(ref _highlightActor);
        }
    }
}
