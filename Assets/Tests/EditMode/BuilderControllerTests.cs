using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class BuilderControllerTests
{
    [Test]
    public void Missing_Resource_Falls_Back_To_Heuristic()
    {
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not found"));
        IBuilderPolicy p = BuilderController.LoadPolicyOrFallback("no_such_policy_resource");
        Assert.IsInstanceOf<HeuristicPolicy>(p);
    }

    [TestCase("")]
    [TestCase("not json at all {{{")]
    [TestCase("{\"type\":\"mlp\",\"weightBits\":[1,2,3]}")]
    public void Garbage_Json_Falls_Back_To_Heuristic(string json)
    {
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("invalid"));
        IBuilderPolicy p = BuilderController.ParseOrFallback(json, "test");
        Assert.IsInstanceOf<HeuristicPolicy>(p);
    }

    [Test]
    public void Shipped_Policy_Resource_Loads_As_Mlp()
    {
        IBuilderPolicy p = BuilderController.LoadPolicyOrFallback(BuilderController.PolicyResource);
        Assert.IsInstanceOf<MlpPolicy>(p);
    }

    [Test]
    public void Toggle_Flips_Enabled_And_Label()
    {
        var go = new GameObject("ctl");
        try
        {
            var c = go.AddComponent<BuilderController>();
            Assert.IsFalse(c.Enabled);
            Assert.AreEqual("AI: OFF", c.Label);
            c.Toggle();
            Assert.IsTrue(c.Enabled);
            Assert.AreEqual("AI: ON", c.Label);
            c.Toggle();
            Assert.IsFalse(c.Enabled);
            Assert.AreEqual("AI: OFF", c.Label);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
