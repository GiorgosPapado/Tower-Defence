using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Auto-play for the running game: every decisionInterval seconds of game time the builder policy
/// (the trained MLP from Resources, or the heuristic as a fallback) may place one turret.
/// Uses the same observation / mask / apply path as EpisodeRunner (BuilderActions.Step).
/// </summary>
public class BuilderController : MonoBehaviour
{
    public const string PolicyResource = "builder_policy";

    /// <summary>Game seconds between two decisions.</summary>
    public float decisionInterval = 1f;
    public KeyCode toggleKey = KeyCode.B;
    /// <summary>Optional text that shows Label (e.g. the toggle button's label).</summary>
    public TMP_Text labelText;

    private bool enabledFlag;
    private IBuilderPolicy policy;
    private BuilderContext ctx;
    private readonly float[] obs = BuilderObservation.Create();
    private readonly bool[] mask = BuilderActions.CreateMask();
    private float sinceDecision;

    public bool Enabled
    {
        get { return enabledFlag; }
        set
        {
            if (value == enabledFlag) return;
            enabledFlag = value;
            sinceDecision = decisionInterval;   // decide on the first frame after enabling
            RefreshLabel();
        }
    }

    public string Label { get { return enabledFlag ? "AI: ON" : "AI: OFF"; } }

    /// <summary>Text currently shown by labelText (null if no text is assigned).</summary>
    public string ShownLabel { get { return labelText == null ? null : labelText.text; } }

    /// <summary>Name of the policy in use ("none" until first needed).</summary>
    public string PolicyName { get { return policy == null ? "none" : policy.GetType().Name; } }

    public void Toggle()
    {
        Enabled = !enabledFlag;
    }

    /// <summary>Replace the policy (null = reload lazily from Resources).</summary>
    public void SetPolicy(IBuilderPolicy p)
    {
        policy = p;
    }

    /// <summary>Load the trained policy from Resources; never throws, falls back to the heuristic.</summary>
    public static IBuilderPolicy LoadPolicyOrFallback(string resourceName)
    {
        TextAsset asset = null;
        try { asset = Resources.Load<TextAsset>(resourceName); }
        catch (Exception e) { Debug.LogWarning("BuilderController: cannot load resource '" + resourceName + "' (" + e.Message + ")."); }
        return ParseOrFallback(asset == null ? null : asset.text, resourceName);
    }

    /// <summary>Parse policy JSON; on null/invalid input log a warning and return a HeuristicPolicy. Never throws.</summary>
    public static IBuilderPolicy ParseOrFallback(string json, string source)
    {
        if (json == null)
        {
            Debug.LogWarning("BuilderController: policy '" + source + "' not found; using HeuristicPolicy.");
            return new HeuristicPolicy();
        }
        try
        {
            return PolicyIO.FromJson(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning("BuilderController: policy '" + source + "' is invalid (" + e.Message + "); using HeuristicPolicy.");
            return new HeuristicPolicy();
        }
    }

    private void Start()
    {
        RefreshLabel();
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey)) Toggle();

        if (!enabledFlag || GameManager.gameEnded || Time.timeScale <= 0f) return;

        sinceDecision += Time.deltaTime;
        if (sinceDecision < decisionInterval) return;
        sinceDecision -= decisionInterval;
        Decide();
    }

    private void Decide()
    {
        if (BuildManager.instance == null) return;
        if (policy == null) policy = LoadPolicyOrFallback(PolicyResource);
        // Unity-null checks: the context is stale after a scene reload (its nodes/shop were destroyed).
        if (ctx == null || ctx.Shop == null || ctx.Slots[0] == null) ctx = BuilderContext.FromScene();
        if (ctx.Shop == null) return;

        int type, cost;
        BuilderActions.Step(policy, ctx, obs, mask, out type, out cost);
    }

    private void RefreshLabel()
    {
        if (labelText != null) labelText.text = Label;
    }
}
