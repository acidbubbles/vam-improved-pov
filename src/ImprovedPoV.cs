//#define POV_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Improved PoV Version 0.0.0
/// By Acidbubbles
/// Possession that actually feels right.
/// Source: https://github.com/acidbubbles/vam-improved-pov
/// </summary>
public class ImprovedPoV : MVRScript
{
    private Atom _person;
    private Camera _mainCamera;
    private Possessor _possessor;
    private FreeControllerV3 _headControl;
    private DAZCharacterSelector _selector;
    private JSONStorableFloat _cameraDepthJSON;
    private JSONStorableFloat _cameraHeightJSON;
    private JSONStorableFloat _cameraPitchJSON;
    private JSONStorableFloat _clipDistanceJSON;
    private JSONStorableBool _autoWorldScaleJSON;
    private JSONStorableBool _possessedOnlyJSON;
    private JSONStorableBool _hideFaceJSON;
    private JSONStorableBool _hideHairJSON;

    private SkinHandler _skinHandler;
    private List<HairHandler> _hairHandlers;
    // For change detection purposes
    private DAZCharacter _character;
    private DAZHairGroup[] _hair;


    // Whether the PoV effects are currently active, i.e. in possession mode
    private bool _lastActive;
    // Requires re-generating all shaders and materials, either because last frame was not ready or because something changed
    private bool _dirty;
    // To avoid spamming errors when something failed
    private bool _failedOnce;
    // When waiting for a model to load, how long before we abandon
    private int _tryAgainAttempts;
    private float _originalWorldScale;

    public override void Init()
    {
        try
        {
            if (containingAtom?.type != "Person")
            {
                SuperController.LogError($"Please apply the ImprovedPoV plugin to the 'Person' atom you wish to possess. Currently applied on '{containingAtom.type}'.");
                DestroyImmediate(this);
                return;
            }

            _person = containingAtom;
            _mainCamera = CameraTarget.centerTarget?.targetCamera;
            _possessor = SuperController
                .FindObjectsOfType(typeof(Possessor))
                .Where(p => p.name == "CenterEye")
                .Select(p => p as Possessor)
                .FirstOrDefault();
            _headControl = (FreeControllerV3)_person.GetStorableByID("headControl");
            _selector = _person.GetComponentInChildren<DAZCharacterSelector>();

            InitControls();
            Camera.onPreRender += OnPreRender;
            Camera.onPostRender += OnPostRender;
        }
        catch (Exception e)
        {
            SuperController.LogError("Failed to initialize Improved PoV: " + e);
            DestroyImmediate(this);
        }
    }

    private void OnPreRender(Camera cam)
    {
        if (!IsPovCamera(cam)) return;

        try
        {
            if (_skinHandler != null)
                _skinHandler.BeforeRender();
            if (_hairHandlers != null)
                _hairHandlers.ForEach(x =>
                {
                    if (x != null)
                        x.BeforeRender();
                });
        }
        catch (Exception e)
        {
            if (_failedOnce) return;
            _failedOnce = true;
            SuperController.LogError("Failed to execute pre render Improved PoV: " + e);
        }
    }

    private void OnPostRender(Camera cam)
    {
        if (!IsPovCamera(cam)) return;

        try
        {
            if (_skinHandler != null)
                _skinHandler.AfterRender();
            if (_hairHandlers != null)
                _hairHandlers.ForEach(x =>
                {
                    if (x != null)
                        x.AfterRender();
                });
        }
        catch (Exception e)
        {
            if (_failedOnce) return;
            _failedOnce = true;
            SuperController.LogError("Failed to execute post render Improved PoV: " + e);
        }
    }

    private bool IsPovCamera(Camera cam)
    {
        return
            // Oculus Rift
            cam.name == "CenterEyeAnchor" ||
            // Steam VR
            cam.name == "Camera (eye)" ||
            // Desktop
            cam.name == "MonitorRig"; /* ||
            // Window Camera
            cam.name == "MiniCamera";
            */
    }

    private void InitControls()
    {
        try
        {
            {
                _cameraDepthJSON = new JSONStorableFloat("Camera depth", 0.054f, 0f, 0.2f, false);
                RegisterFloat(_cameraDepthJSON);
                var cameraDepthSlider = CreateSlider(_cameraDepthJSON, false);
                cameraDepthSlider.slider.onValueChanged.AddListener(delegate (float val)
                {
                    ApplyCameraPosition(_lastActive);
                });
            }

            {
                _cameraHeightJSON = new JSONStorableFloat("Camera height", 0f, -0.05f, 0.05f, false);
                RegisterFloat(_cameraHeightJSON);
                var cameraHeightSlider = CreateSlider(_cameraHeightJSON, false);
                cameraHeightSlider.slider.onValueChanged.AddListener(delegate (float val)
                {
                    ApplyCameraPosition(_lastActive);
                });
            }

            {
                _cameraPitchJSON = new JSONStorableFloat("Camera pitch", 0f, -135f, 45f, true);
                RegisterFloat(_cameraPitchJSON);
                var cameraPitchSlider = CreateSlider(_cameraPitchJSON, false);
                cameraPitchSlider.slider.onValueChanged.AddListener(delegate (float val)
                {
                    ApplyCameraPosition(_lastActive);
                });
            }

            {
                _clipDistanceJSON = new JSONStorableFloat("Clip distance", 0.01f, 0.01f, .2f, true);
                RegisterFloat(_clipDistanceJSON);
                var clipDistanceSlider = CreateSlider(_clipDistanceJSON, false);
                clipDistanceSlider.slider.onValueChanged.AddListener(delegate (float val)
                {
                    ApplyCameraPosition(_lastActive);
                });
            }

            {
                _autoWorldScaleJSON = new JSONStorableBool("Auto world scale", false);
                RegisterBool(_autoWorldScaleJSON);
                var autoWorldScaleToggle = CreateToggle(_autoWorldScaleJSON, true);
                autoWorldScaleToggle.toggle.onValueChanged.AddListener(delegate (bool val)
                {
                    _dirty = true;
                });
            }

            {
                var possessedOnlyDefaultValue = true;
#if (POV_DIAGNOSTICS)
                // NOTE: Easier to test when it's always on
                possessedOnlyDefaultValue = false;
#endif
                _possessedOnlyJSON = new JSONStorableBool("Activate only when possessed", possessedOnlyDefaultValue);
                RegisterBool(_possessedOnlyJSON);
                var possessedOnlyCheckbox = CreateToggle(_possessedOnlyJSON, true);
                possessedOnlyCheckbox.toggle.onValueChanged.AddListener(delegate (bool val)
                {
                    _dirty = true;
                });
            }

            {
                _hideFaceJSON = new JSONStorableBool("Hide face", true);
                RegisterBool(_hideFaceJSON);
                var hideFaceToggle = CreateToggle(_hideFaceJSON, true);
                hideFaceToggle.toggle.onValueChanged.AddListener(delegate (bool val)
                {
                    _dirty = true;
                });
            }

            {
                _hideHairJSON = new JSONStorableBool("Hide hair", true);
                RegisterBool(_hideHairJSON);
                var hideHairToggle = CreateToggle(_hideHairJSON, true);
                hideHairToggle.toggle.onValueChanged.AddListener(delegate (bool val)
                {
                    _dirty = true;
                });
            }
        }
        catch (Exception e)
        {
            SuperController.LogError("Failed to register controls: " + e);
        }
    }

    public void OnDisable()
    {
        try
        {
            _dirty = false;
            ApplyAll(false);
            _lastActive = false;
        }
        catch (Exception e)
        {
            SuperController.LogError("Failed to disable Improved PoV: " + e);
        }
    }

    public void OnDestroy()
    {
        OnDisable();
        Camera.onPreRender -= OnPreRender;
        Camera.onPostRender -= OnPostRender;
    }

    public void Update()
    {
        try
        {
            var active = _headControl.possessed || !_possessedOnlyJSON.val;

            if (!_lastActive && active)
            {
                ApplyAll(true);
                _lastActive = true;
            }
            else if (_lastActive && !active)
            {
                ApplyAll(false);
                _lastActive = false;
            }
            else if (_dirty)
            {
                _dirty = false;
                ApplyAll(_lastActive);
            }
            else if (_lastActive && _selector.selectedCharacter != _character)
            {
                _skinHandler?.Restore();
                _skinHandler = null;
                ApplyAll(true);
            }
            else if (_lastActive && !_selector.hairItems.Where(h => h.active).SequenceEqual(_hair))
            {
                // Note: This only checks if the first hair changed. It'll be good enough for most purposes, but imperfect.
                if (_hairHandlers != null)
                {
                    _hairHandlers.ForEach(x =>
                    {
                        if (x != null)
                            x.Restore();
                    });
                    _hairHandlers = null;
                }
                ApplyAll(true);
            }
        }
        catch (Exception e)
        {
            if (_failedOnce) return;
            _failedOnce = true;
            SuperController.LogError("Failed to update Improved PoV: " + e);
        }
    }

    private void ApplyAll(bool active)
    {
        // Try again next frame
        if (_selector.selectedCharacter?.skin == null)
        {
            MakeDirty("Skin not yet loaded.");
            return;
        }

        _character = _selector.selectedCharacter;
        _hair = _selector.hairItems.Where(h => h.active).ToArray();

        ApplyAutoWorldScale(active);
        ApplyCameraPosition(active);
        ApplyPossessorMeshVisibility(active);
        if (UpdateHandler(ref _skinHandler, active && _hideFaceJSON.val))
            ConfigureHandler("Skin", ref _skinHandler, _skinHandler.Configure(_character.skin));
        if (_hairHandlers == null)
            _hairHandlers = new List<HairHandler>(new HairHandler[_hair.Length]);
        for (var i = 0; i < _hairHandlers.Count; i++)
        {
            var hairHandler = _hairHandlers[i];
            if (UpdateHandler(ref hairHandler, active && _hideHairJSON.val))
                ConfigureHandler("Hair", ref hairHandler, hairHandler.Configure(_hair[i]));
            _hairHandlers[i] = hairHandler;
        }

        if (!_dirty) _tryAgainAttempts = 0;
    }

    private void MakeDirty(string reason)
    {
        _dirty = true;
        _tryAgainAttempts++;
        if (_tryAgainAttempts > 90 * 20) // Approximately 20 to 40 seconds
        {
            SuperController.LogError("Failed to apply ImprovedPoV. Reason: " + reason + ". Try reloading the plugin, or report the issue to @Acidbubbles.");
            enabled = false;
        }
    }

    private void ConfigureHandler<T>(string what, ref T handler, int result)
     where T : IHandler, new()
    {
        switch (result)
        {
            case HandlerConfigurationResult.Success:
                break;
            case HandlerConfigurationResult.CannotApply:
                handler = default(T);
                break;
            case HandlerConfigurationResult.TryAgainLater:
                handler = default(T);
                MakeDirty(what + " is still waiting for assets to be ready.");
                break;
        }
    }

    private bool UpdateHandler<T>(ref T handler, bool active)
     where T : IHandler, new()
    {
        if (handler == null && active)
        {
            handler = new T();
            return true;
        }

        if (handler != null && active)
        {
            handler.Restore();
            handler = new T();
            return true;
        }

        if (handler != null && !active)
        {
            handler.Restore();
            handler = default(T);
        }

        return false;
    }

    private void ApplyCameraPosition(bool active)
    {
        try
        {
            _mainCamera.nearClipPlane = active ? _clipDistanceJSON.val : 0.01f;

            var cameraDepth = active ? _cameraDepthJSON.val : 0;
            var cameraHeight = active ? _cameraHeightJSON.val : 0;
            var cameraPitch = active ? _cameraPitchJSON.val : 0;
            var pos = _possessor.transform.position;
            _mainCamera.transform.position = pos - _mainCamera.transform.rotation * Vector3.forward * cameraDepth - _mainCamera.transform.rotation * Vector3.down * cameraHeight;
            _possessor.transform.localEulerAngles = new Vector3(cameraPitch, 0f, 0f);
            _possessor.transform.position = pos;
        }
        catch (Exception e)
        {
            SuperController.LogError("Failed to update camera position: " + e);
        }
    }

    private void ApplyPossessorMeshVisibility(bool active)
    {
        try
        {
            var meshActive = !active;

            _possessor.gameObject.transform.Find("Capsule")?.gameObject.SetActive(meshActive);
            _possessor.gameObject.transform.Find("Sphere1")?.gameObject.SetActive(meshActive);
            _possessor.gameObject.transform.Find("Sphere2")?.gameObject.SetActive(meshActive);
        }
        catch (Exception e)
        {
            SuperController.LogError("Failed to update possessor mesh visibility: " + e);
        }
    }

    private void ApplyAutoWorldScale(bool active)
    {
        if (!active)
        {
            if (_originalWorldScale != 0f && SuperController.singleton.worldScale != _originalWorldScale)
            {
                SuperController.singleton.worldScale = _originalWorldScale;
                _originalWorldScale = 0f;
            }
            return;
        }

        if (!_autoWorldScaleJSON.val) return;

        if (_originalWorldScale == 0f)
        {
            _originalWorldScale = SuperController.singleton.worldScale;
        }

        var eyes = _person.GetComponentsInChildren<LookAtWithLimits>();
        var lEye = eyes.FirstOrDefault(eye => eye.name == "lEye");
        var rEye = eyes.FirstOrDefault(eye => eye.name == "rEye");
        if (lEye == null || rEye == null)
            return;
        var atomEyeDistance = Vector3.Distance(lEye.transform.position, rEye.transform.position);

        var rig = GameObject.FindObjectOfType<OVRCameraRig>();
        if (rig == null)
            return;
        var rigEyesDistance = Vector3.Distance(rig.leftEyeAnchor.transform.position, rig.rightEyeAnchor.transform.position);

        var scale = atomEyeDistance / rigEyesDistance;
        var worldScale = SuperController.singleton.worldScale * scale;

        if (SuperController.singleton.worldScale != worldScale)
            SuperController.singleton.worldScale = worldScale;

        var yAdjust = _possessor.autoSnapPoint.position.y - _headControl.possessPoint.position.y;

        if (yAdjust != 0)
            SuperController.singleton.playerHeightAdjust = SuperController.singleton.playerHeightAdjust - yAdjust;
    }

    public static class HandlerConfigurationResult
    {
        public const int Success = 0;
        public const int CannotApply = 1;
        public const int TryAgainLater = 2;
    }

    public interface IHandler
    {
        void Restore();
        void BeforeRender();
        void AfterRender();
    }

    public class SkinHandler : IHandler
    {
        public class SkinShaderMaterialReference
        {
            public Material material;
            public Shader originalShader;
            public float originalAlphaAdjust;
            public float originalColorAlpha;
            public Color originalSpecColor;

            public static SkinShaderMaterialReference FromMaterial(Material material)
            {
                return new SkinShaderMaterialReference
                {
                    material = material,
                    originalShader = material.shader,
                    originalAlphaAdjust = material.GetFloat("_AlphaAdjust"),
                    originalColorAlpha = material.GetColor("_Color").a,
                    originalSpecColor = material.GetColor("_SpecColor")
                };
            }
        }

        public static readonly string[] MaterialsToHide = new[]
        {
            "Lacrimals",
            "Pupils",
            "Lips",
            "Gums",
            "Irises",
            "Teeth",
            "Face",
            "Head",
            "InnerMouth",
            "Tongue",
            "EyeReflection",
            "Nostrils",
            "Cornea",
            "Eyelashes",
            "Sclera",
            "Ears",
            "Tear"
        };

        public static IList<Material> GetMaterialsToHide(DAZSkinV2 skin)
        {
#if (POV_DIAGNOSTICS)
            if (skin == null) throw new NullReferenceException("skin is null");
            if (skin.GPUmaterials == null) throw new NullReferenceException("skin materials are null");
#endif

            var materials = new List<Material>(MaterialsToHide.Length);

            foreach (var material in skin.GPUmaterials)
            {
                if (!MaterialsToHide.Any(materialToHide => material.name.StartsWith(materialToHide)))
                    continue;

                materials.Add(material);
            }

#if (POV_DIAGNOSTICS)
            // NOTE: Tear is not on all models
            if (materials.Count < MaterialsToHide.Length - 1)
                throw new Exception("Not enough materials found to hide. List: " + string.Join(", ", skin.GPUmaterials.Select(m => m.name).ToArray()));
#endif

            return materials;
        }

        private static readonly Dictionary<string, Shader> ReplacementShaders = new Dictionary<string, Shader>
            {
                // Opaque materials
                { "Custom/Subsurface/GlossCullComputeBuff", Shader.Find("Custom/Subsurface/TransparentGlossSeparateAlphaComputeBuff") },
                { "Custom/Subsurface/GlossNMCullComputeBuff", Shader.Find("Custom/Subsurface/TransparentGlossNMSeparateAlphaComputeBuff") },
                { "Custom/Subsurface/GlossNMDetailCullComputeBuff", Shader.Find("Custom/Subsurface/TransparentGlossNMDetailNoCullSeparateAlphaComputeBuff") },
                { "Custom/Subsurface/CullComputeBuff", Shader.Find("Custom/Subsurface/TransparentSeparateAlphaComputeBuff") },

                // Transparent materials
                { "Custom/Subsurface/TransparentGlossNoCullSeparateAlphaComputeBuff", null },
                { "Custom/Subsurface/TransparentGlossComputeBuff", null },
                { "Custom/Subsurface/TransparentComputeBuff", null },
                { "Custom/Subsurface/AlphaMaskComputeBuff", null },
                { "Marmoset/Transparent/Simple Glass/Specular IBLComputeBuff", null },
            };

        private DAZSkinV2 _skin;
        private List<SkinShaderMaterialReference> _materialRefs;

        public int Configure(DAZSkinV2 skin)
        {
            _skin = skin;
            _materialRefs = new List<SkinShaderMaterialReference>();

            foreach (var material in GetMaterialsToHide(skin))
            {
#if (IMPROVED_POV)
                if(material == null)
                    throw new InvalidOperationException("Attempts to apply the shader strategy on a destroyed material.");

                if (material.GetInt(SkinShaderMaterialReference.ImprovedPovEnabledShaderKey) == 1)
                    throw new InvalidOperationException("Attempts to apply the shader strategy on a skin that already has the plugin enabled (shader key).");
#endif

                var materialInfo = SkinShaderMaterialReference.FromMaterial(material);

                Shader shader;
                if (!ReplacementShaders.TryGetValue(material.shader.name, out shader))
                    SuperController.LogError("Missing replacement shader: '" + material.shader.name + "'");

                if (shader != null) material.shader = shader;

                _materialRefs.Add(materialInfo);
            }

            // This is a hack to force a refresh of the shaders cache
            skin.BroadcastMessage("OnApplicationFocus", true);
            return HandlerConfigurationResult.Success;
        }

        public void Restore()
        {
            foreach (var material in _materialRefs)
                material.material.shader = material.originalShader;

            _materialRefs = null;

            // This is a hack to force a refresh of the shaders cache
            _skin.BroadcastMessage("OnApplicationFocus", true);
        }

        public void BeforeRender()
        {
            foreach (var materialRef in _materialRefs)
            {
                var material = materialRef.material;
                material.SetFloat("_AlphaAdjust", -1f);
                var color = material.GetColor("_Color");
                material.SetColor("_Color", new Color(color.r, color.g, color.b, 0f));
                material.SetColor("_SpecColor", new Color(0f, 0f, 0f, 0f));
            }
        }

        public void AfterRender()
        {
            foreach (var materialRef in _materialRefs)
            {
                var material = materialRef.material;
                material.SetFloat("_AlphaAdjust", materialRef.originalAlphaAdjust);
                var color = material.GetColor("_Color");
                material.SetColor("_Color", new Color(color.r, color.g, color.b, materialRef.originalColorAlpha));
                material.SetColor("_SpecColor", materialRef.originalSpecColor);
            }
        }
    }

    public class HairHandler : IHandler
    {
        public class MaterialReference
        {
            public Material material;
            public float originalAlphaAdjust;
        }

        private Material _hairMaterial;
        private string _hairShaderProperty;
        private float _hairShaderHiddenValue;
        private float _hairShaderOriginalValue;
        private List<MaterialReference> _materialRefs;

        public int Configure(DAZHairGroup hair)
        {
            if (hair == null || hair.name == "NoHair")
                return HandlerConfigurationResult.CannotApply;

            if (hair.name == "Sim2Hair" || hair.name == "Sim2HairMale" || hair.name == "CustomHairItem")
                return ConfigureSimV2Hair(hair);
            else if (hair.name == "SimHairGroup" || hair.name == "SimHairGroup2")
                return ConfigureSimHair(hair);
            else
                return ConfigureSimpleHair(hair);
        }

        private int ConfigureSimV2Hair(DAZHairGroup hair)
        {
            var materialRefs = new List<MaterialReference>(GetScalpMaterialReferences(hair));
            if (materialRefs.Count != 0) _materialRefs = materialRefs;

            var hairMaterial = hair.GetComponentInChildren<MeshRenderer>()?.material;
            if (hairMaterial == null)
                return HandlerConfigurationResult.TryAgainLater;

            _hairMaterial = hairMaterial;
            _hairShaderProperty = "_StandWidth";
            _hairShaderHiddenValue = 0f;
            _hairShaderOriginalValue = _hairMaterial.GetFloat(_hairShaderProperty);
            return HandlerConfigurationResult.Success;
        }

        private int ConfigureSimHair(DAZHairGroup hair)
        {
            SuperController.LogError("Hair " + hair.name + " is not supported!");
            return HandlerConfigurationResult.CannotApply;
        }

        private int ConfigureSimpleHair(DAZHairGroup hair)
        {
            var materialRefs = hair.GetComponentsInChildren<DAZMesh>()
                .SelectMany(m => m.materials)
                .Distinct()
                .Select(m => new MaterialReference
                {
                    material = m,
                    originalAlphaAdjust = m.GetFloat("_AlphaAdjust")
                })
                .ToList();

            if (materialRefs.Count == 0)
                return HandlerConfigurationResult.TryAgainLater;

            materialRefs.AddRange(GetScalpMaterialReferences(hair));

            _materialRefs = materialRefs;

            return HandlerConfigurationResult.Success;
        }

        private IEnumerable<MaterialReference> GetScalpMaterialReferences(DAZHairGroup hair)
        {
            return hair.GetComponentsInChildren<DAZSkinWrap>()
                .SelectMany(m => m.GPUmaterials)
                .Distinct()
                .Select(m => new MaterialReference
                {
                    material = m,
                    originalAlphaAdjust = m.GetFloat("_AlphaAdjust")
                });
        }

        public void Restore()
        {
            _hairMaterial = null;
            _materialRefs = null;
        }

        public void BeforeRender()
        {
            if (_hairMaterial != null)
                _hairMaterial.SetFloat(_hairShaderProperty, _hairShaderHiddenValue);
            if (_materialRefs != null)
                foreach (var materialRef in _materialRefs)
                    materialRef.material.SetFloat("_AlphaAdjust", -1f);
        }

        public void AfterRender()
        {
            if (_hairMaterial != null)
                _hairMaterial.SetFloat(_hairShaderProperty, _hairShaderOriginalValue);
            if (_materialRefs != null)
                foreach (var materialRef in _materialRefs)
                    materialRef.material.SetFloat("_AlphaAdjust", materialRef.originalAlphaAdjust);
        }
    }
}
