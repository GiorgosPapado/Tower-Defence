using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Training provenance stored next to the weights.</summary>
[Serializable]
public class PolicyMetadata
{
    public int seed;
    public int generations;
    public int population;
    public float trainReturn;
    public string unityVersion;
    public string createdUtc;
    public string config;

    public static PolicyMetadata Create(int seed, int generations, int population, string config)
    {
        return new PolicyMetadata
        {
            seed = seed,
            generations = generations,
            population = population,
            unityVersion = Application.unityVersion,
            createdUtc = DateTime.UtcNow.ToString("o"),
            config = config ?? "",
        };
    }
}

/// <summary>On-disk layout of a saved MLP policy.</summary>
[Serializable]
public class PolicyFile
{
    public string type = "mlp";
    public int formatVersion = 1;
    public int specVersion;
    public int obsSize;
    public int hidden;
    public int actions;
    public PolicyMetadata metadata;

    /// <summary>The float parameters stored as their raw IEEE-754 bit patterns, so the round trip is bit-exact.</summary>
    public int[] weightBits;
}

/// <summary>JSON save/load for policies. Nothing here runs at import or static-init time.</summary>
public static class PolicyIO
{
    public const string PolicyType = "mlp";
    public const int FormatVersion = 1;

    public static string ToJson(MlpPolicy policy, PolicyMetadata metadata)
    {
        float[] w = policy.GetParameters();
        var bits = new int[w.Length];
        for (int i = 0; i < w.Length; i++) bits[i] = BitConverter.SingleToInt32Bits(w[i]);

        var file = new PolicyFile
        {
            type = PolicyType,
            formatVersion = FormatVersion,
            specVersion = BuilderSpec.SpecVersion,
            obsSize = policy.ObsSize,
            hidden = policy.Hidden,
            actions = policy.Actions,
            metadata = metadata ?? new PolicyMetadata(),
            weightBits = bits,
        };
        return JsonUtility.ToJson(file);
    }

    /// <summary>Parse and validate a policy JSON. Throws FormatException if it does not match the current BuilderSpec.</summary>
    public static MlpPolicy FromJson(string json, out PolicyMetadata metadata)
    {
        PolicyFile file = null;
        try
        {
            file = JsonUtility.FromJson<PolicyFile>(json);
        }
        catch (ArgumentException e)
        {
            throw new FormatException("Policy JSON is not valid: " + e.Message, e);
        }

        if (file == null || file.weightBits == null)
            throw new FormatException("Policy JSON is empty or has no weights");
        if (file.type != PolicyType)
            throw new FormatException("Unsupported policy type '" + file.type + "'");
        if (file.formatVersion != FormatVersion)
            throw new FormatException("Unsupported policy format version " + file.formatVersion);
        if (file.specVersion != BuilderSpec.SpecVersion || file.obsSize != BuilderSpec.ObsSize || file.actions != BuilderSpec.ActionCount)
            throw new FormatException("Policy was trained for a different BuilderSpec (spec " + file.specVersion
                + ", obs " + file.obsSize + ", actions " + file.actions + ")");
        if (file.hidden <= 0 || file.weightBits.Length != MlpPolicy.ParamCount(file.obsSize, file.hidden, file.actions))
            throw new FormatException("Policy weight count does not match its dimensions");

        var policy = new MlpPolicy(file.obsSize, file.hidden, file.actions);
        var w = new float[file.weightBits.Length];
        for (int i = 0; i < w.Length; i++) w[i] = BitConverter.Int32BitsToSingle(file.weightBits[i]);
        policy.SetParameters(w);

        metadata = file.metadata ?? new PolicyMetadata();
        return policy;
    }

    public static MlpPolicy FromJson(string json)
    {
        PolicyMetadata ignored;
        return FromJson(json, out ignored);
    }

    public static void Save(string path, MlpPolicy policy, PolicyMetadata metadata)
    {
        string dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, ToJson(policy, metadata), new UTF8Encoding(false));
    }

    public static MlpPolicy Load(string path, out PolicyMetadata metadata)
    {
        return FromJson(File.ReadAllText(path, Encoding.UTF8), out metadata);
    }

    /// <summary>Load a policy shipped as a TextAsset under a Resources folder (name without extension). Null if missing.</summary>
    public static MlpPolicy LoadResource(string resourceName, out PolicyMetadata metadata)
    {
        metadata = null;
        var asset = Resources.Load<TextAsset>(resourceName);
        if (asset == null) return null;
        return FromJson(asset.text, out metadata);
    }
}
