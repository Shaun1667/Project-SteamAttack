using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEngine.SceneManagement;

/// <summary>기존 발사 이벤트를 유지하며 YPH 총 이펙트 에셋을 레퍼런스에 맞춰 갱신합니다.</summary>
public sealed class YPH_ReferenceGunFxBuilder
{
    private const string FxFolder = "Assets/VFX/YPH/";
    private const string GunPath = "Assets/Prefabs/YPH/YPH_SteamGunProxy.prefab";
    private Material _flash, _steam, _spark;

    /// <summary>저장된 Edit 상태에서 전용 머티리얼과 기존 세 이펙트 프리팹을 갱신합니다.</summary>
    [MenuItem("Tools/YPH/Apply Reference Gun FX")]
    public static void ApplyReference()
    {
        new YPH_ReferenceGunFxBuilder().Build();
    }

    /// <summary>총 전용 재료와 세 이펙트를 생성하고 기존 발사 목록에 연결합니다.</summary>
    private void Build()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling)
        { throw new Exception("Edit mode, no compilation required"); }
        for (int i = 0; i < SceneManager.sceneCount; i++)
        { if (SceneManager.GetSceneAt(i).isDirty) { throw new Exception("Save user scene before asset update"); } }

        AssetDatabase.Refresh();
        var flashTexture = Import("YPH_T_GunMuzzleFlash_AI");
        var steamTexture = Import("YPH_T_GunSteam_AI");
        _flash = MakeMaterial("YPH_M_GunFlash", flashTexture, true);
        _steam = MakeMaterial("YPH_M_GunSteam", steamTexture, false);
        _spark = MakeMaterial("YPH_M_GunMetalSpark", null, true);
        UpdateMuzzle();
        UpdateImpact("ImpactEnemy", true);
        UpdateImpact("ImpactSurface", false);

        // 새 레이어까지 발사 목록에 연결합니다. 기존 총·탱크·입력·피해 판정은 바꾸지 않습니다.
        var gunRoot = PrefabUtility.LoadPrefabContents(GunPath);
        try
        {
            var layers = gunRoot.transform.Find("Body/Muzzle/MuzzleFlash").GetComponentsInChildren<ParticleSystem>(true);
            var serialized = new SerializedObject(gunRoot.GetComponent<YPH_GunMuzzleFlashView>());
            var array = serialized.FindProperty("_effects");
            array.arraySize = layers.Length;
            for (int i = 0; i < layers.Length; i++) { array.GetArrayElementAtIndex(i).objectReferenceValue = layers[i]; }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(gunRoot, GunPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(gunRoot); }
        AssetDatabase.SaveAssets();
        SessionState.SetString("YPH_GunReferencePreviewLabel", "After");
        Debug.Log("YPH Reference Gun FX: 총구 섬광·흰 증기·금속 스파크 적용 완료");
    }

    /// <summary>AI 텍스처를 알파가 있는 일반 텍스처로 가져오고 게임용 최대 크기를 제한합니다.</summary>
    private Texture2D Import(string name)
    {
        string path = "Assets/Textures/YPH/" + name + ".png";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) { throw new Exception("Missing generated texture " + path); }
        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>총 전용 머티리얼을 만들어 공유 중인 수류탄·배출구 머티리얼에 영향을 주지 않습니다.</summary>
    private Material MakeMaterial(string name, Texture2D texture, bool additive)
    {
        string path = "Assets/Materials/YPH/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find(texture != null ? "Universal Render Pipeline/Particles/Unlit" : "YPH/SoftParticle");
        if (shader == null) { throw new Exception("Missing particle shader"); }
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        material.renderQueue = 3000;
        if (texture != null)
        {
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 2f : 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.SetShaderPassEnabled("DepthOnly", false);
        }
        else { material.SetFloat("_NoiseStrength", 0f); material.SetFloat("_Softness", 1.2f); }
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>섬광·전방 화염·증기·잔불을 발사 순간부터 서로 다른 시간으로 사라지게 합니다.</summary>
    private void UpdateMuzzle()
    {
        string path = FxFolder + "YPH_VFX_MuzzleFlash.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var flash = Layer(root, "Flash");
            Configure(flash, _flash, 1, 0.075f, 0.58f, 0f, new Color(2.1f, 1.9f, 1.5f));
            var main = flash.main; main.startRotation = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            Grow(flash, new Keyframe(0, 0.45f), new Keyframe(0.18f, 1f), new Keyframe(1, 0.45f));
            Fade(flash, Color.white, new Color(1f, 0.65f, 0.25f), 1f);

            var flame = Layer(root, "AxialFlame");
            Configure(flame, _flash, 1, 0.07f, 0.28f, 2f, new Color(1.8f, 1.6f, 1.2f));
            Cone(flame, 4f, 0.01f);
            flame.transform.localPosition = Vector3.forward * 0.06f;

            var core = Layer(root, "Core");
            Configure(core, _spark, 1, 0.035f, 0.17f, 0f, new Color(3f, 2.7f, 2f));
            Grow(core, new Keyframe(0, 0.8f), new Keyframe(1, 0.1f));

            var steam = Layer(root, "Steam");
            Configure(steam, _steam, 9, 0.8f, 0.19f, 1f, new Color(0.9f, 0.94f, 1f, 0.38f));
            main = steam.main;
            main.startDelay = 0.02f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.65f, 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.13f, 0.24f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            main.gravityModifier = -0.025f;
            Cone(steam, 19f, 0.025f);
            Grow(steam, new Keyframe(0, 0.5f), new Keyframe(0.18f, 1.4f), new Keyframe(1, 3.2f));
            var noise = steam.noise; noise.enabled = true; noise.strength = 0.16f; noise.frequency = 2f; noise.scrollSpeed = 0.7f;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            var fade = steam.colorOverLifetime; fade.color = Gradient(Color.white, new Color(0.8f, 0.88f, 1f),
                new GradientAlphaKey(0, 0), new GradientAlphaKey(0.7f, 0.08f), new GradientAlphaKey(0.4f, 0.4f), new GradientAlphaKey(0, 1));

            var muzzleSparks = Layer(root, "Sparks");
            Sparks(muzzleSparks, 5, 0.22f, 4f, 23f);
            Stretch(muzzleSparks, 0.02f, 2f);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <summary>적·지형 모두 충돌면 바깥으로 금속 스파크가 퍼지게 하며 큰 폭발·파동은 추가하지 않습니다.</summary>
    private void UpdateImpact(string name, bool enemy)
    {
        string path = FxFolder + "YPH_VFX_" + name + ".prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Configure(enemy ? root.GetComponent<ParticleSystem>() : Layer(root, "Core"), _spark, 1, 0.045f, 0.18f, 0f, new Color(3f, 2.8f, 2.2f));
            Sparks(enemy ? Layer(root, "Sparks") : root.GetComponent<ParticleSystem>(), 16, 0.55f, 6.5f, 82f);
            var embers = Layer(root, "Embers");
            Sparks(embers, 8, 0.4f, 2.5f, 85f);
            var renderer = embers.GetComponent<ParticleSystemRenderer>(); renderer.velocityScale = 0.04f; renderer.lengthScale = 2f;
            if (root.transform.Find("Dust") != null) { root.transform.Find("Dust").gameObject.SetActive(false); }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <summary>기존 레이어를 재사용하고 필요한 파티클만 추가합니다.</summary>
    private ParticleSystem Layer(GameObject root, string name)
    {
        var child = root.transform.Find(name);
        if (child != null) { return child.GetComponent<ParticleSystem>(); }
        var go = new GameObject(name); go.transform.SetParent(root.transform, false);
        return go.AddComponent<ParticleSystem>();
    }

    /// <summary>자기 재생 없이 실제 이벤트에서 한 번 방출되는 월드 좌표 입자로 설정합니다.</summary>
    private void Configure(ParticleSystem ps, Material material, short count, float life, float size, float speed, Color color)
    {
        ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = false; main.playOnAwake = false; main.stopAction = ParticleSystemStopAction.None;
        main.duration = Mathf.Max(0.1f, life); main.startDelay = 0f; main.startLifetime = life;
        main.startSize = size; main.startSpeed = speed; main.startColor = color;
        main.maxParticles = count; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var emission = ps.emission; emission.enabled = true; emission.rateOverTime = 0f; emission.rateOverDistance = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });
        var shape = ps.shape; shape.enabled = speed > 0f;
        var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        Fade(ps, Color.white, Color.white, 1f);
        Grow(ps, new Keyframe(0, 1), new Keyframe(1, 0.3f));
    }

    /// <summary>입자를 충돌면의 법선 또는 총구 전방으로 퍼뜨립니다.</summary>
    private void Cone(ParticleSystem ps, float angle, float radius)
    { var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = angle; shape.radius = radius; }

    /// <summary>속도 방향으로 늘어난 가는 선을 만들어 금속 스파크를 점과 구분합니다.</summary>
    private void Stretch(ParticleSystem ps, float velocityScale, float lengthScale)
    { var renderer = ps.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Stretch; renderer.velocityScale = velocityScale; renderer.lengthScale = lengthScale; }

    /// <summary>주황색 스파크가 붉게 식으며 중력에 따라 떨어지게 합니다. 중심 섬광은 별도 입자입니다.</summary>
    private void Sparks(ParticleSystem ps, short count, float life, float speed, float angle)
    {
        Configure(ps, _spark, count, life, 0.035f, speed, Color.white);
        var main = ps.main; main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.45f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed); main.gravityModifier = 0.6f;
        Cone(ps, angle, 0.012f); Stretch(ps, 0.12f, 4f);
        Grow(ps, new Keyframe(0, 1), new Keyframe(0.6f, 0.7f), new Keyframe(1, 0.05f));
        // 파티클 정점 색은 0~1로 저장되므로 HDR 값을 쓰면 초록 채널도 포화되어 노랗게 변합니다.
        Fade(ps, new Color(1f, 0.32f, 0.02f), new Color(0.7f, 0.045f, 0.005f), 1f);
    }

    /// <summary>정규화된 생존 시간에 맞춰 입자 크기를 바꿉니다.</summary>
    private void Grow(ParticleSystem ps, params Keyframe[] keys)
    { var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(keys)); }

    /// <summary>색이 식는 동안 끝부분 알파가 0으로 사라지게 합니다.</summary>
    private void Fade(ParticleSystem ps, Color start, Color end, float alpha)
    { var fade = ps.colorOverLifetime; fade.enabled = true; fade.color = Gradient(start, end, new GradientAlphaKey(alpha, 0), new GradientAlphaKey(alpha, 0.15f), new GradientAlphaKey(0, 1)); }

    /// <summary>입자 색과 투명도 곡선을 함께 구성합니다.</summary>
    private Gradient Gradient(Color start, Color end, params GradientAlphaKey[] alpha)
    { var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(start, 0), new GradientColorKey(end, 1) }, alpha); return gradient; }
}
