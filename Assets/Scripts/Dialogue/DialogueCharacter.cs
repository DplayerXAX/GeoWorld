using System;
using System.Collections.Generic;
using UnityEngine;

// A speaker: display name, name colour, and a set of named portraits (立绘 / expressions).
// Portable: pure data, no project dependencies. Create via Assets ▸ Create ▸ Dialogue ▸ Character.
[CreateAssetMenu(menuName = "Dialogue/Character", fileName = "Character")]
public class DialogueCharacter : ScriptableObject
{
    public string displayName = "Name";
    public Color  nameColor   = new Color(0.086f, 0.086f, 0.086f);

    [Serializable]
    public class Portrait
    {
        [Tooltip("Expression key referenced by a line, e.g. 'happy', 'angry'. First entry is the default.")]
        public string key = "default";
        public Sprite sprite;
    }

    public List<Portrait> portraits = new();

    // Returns the portrait for `key`, or the first one as a fallback.
    public Sprite GetPortrait(string key)
    {
        if (portraits == null || portraits.Count == 0) return null;
        if (!string.IsNullOrEmpty(key))
            foreach (var p in portraits)
                if (p != null && p.key == key) return p.sprite;
        return portraits[0]?.sprite;
    }
}
