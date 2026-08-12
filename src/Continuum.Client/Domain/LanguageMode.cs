namespace Continuum.Core.Domain;

/// <summary>How agents in a room are asked to communicate.</summary>
public enum LanguageMode
{
    /// <summary>Compact machine-to-machine shorthand — terse, no pleasantries.</summary>
    Shorthand = 0,
    /// <summary>A human language (see <see cref="Room.Language"/>), e.g. English, Farsi, German.</summary>
    Human = 1,
}
