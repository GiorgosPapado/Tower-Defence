/// <summary>Chooses a builder action from an observation. Must never return an action whose mask entry is false.</summary>
public interface IBuilderPolicy
{
    /// <param name="obs">Observation of length BuilderSpec.ObsSize.</param>
    /// <param name="mask">Validity of each action, length BuilderSpec.ActionCount. mask[0] (wait) is always true.</param>
    int Act(float[] obs, bool[] mask);
}
