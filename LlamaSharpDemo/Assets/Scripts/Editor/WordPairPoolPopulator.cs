using UnityEditor;
using UnityEngine;
using DoodleDiplomacy.Data;

namespace DoodleDiplomacy.Editor
{
    public static class WordPairPoolPopulator
    {
        [MenuItem("DoodleDiplomacy/Create Word Pair Pool Asset")]
        public static void CreateWordPairPoolAsset()
        {
            var pool = ScriptableObject.CreateInstance<WordPairPool>();

            var pairs = new WordPair[]
            {
                new WordPair { wordA = "dog",      wordB = "lantern"   },
                new WordPair { wordA = "wolf",     wordB = "rope"      },
                new WordPair { wordA = "crow",     wordB = "bell"      },
                new WordPair { wordA = "snake",    wordB = "torch"     },
                new WordPair { wordA = "horse",    wordB = "chest"     },
                new WordPair { wordA = "fish",     wordB = "barrel"    },
                new WordPair { wordA = "bear",     wordB = "drum"      },
                new WordPair { wordA = "owl",      wordB = "pillow"    },

                new WordPair { wordA = "sword",    wordB = "cloak"     },
                new WordPair { wordA = "torch",    wordB = "cage"      },
                new WordPair { wordA = "rope",     wordB = "crown"     },
                new WordPair { wordA = "axe",      wordB = "wheel"     },
                new WordPair { wordA = "shield",   wordB = "flower"    },
                new WordPair { wordA = "hammer",   wordB = "bell"      },
                new WordPair { wordA = "spear",    wordB = "bowl"      },
                new WordPair { wordA = "knife",    wordB = "bread"     },
                new WordPair { wordA = "chain",    wordB = "helmet"    },
                new WordPair { wordA = "staff",    wordB = "boat"      },

                new WordPair { wordA = "boat",     wordB = "cage"      },
                new WordPair { wordA = "tent",     wordB = "lantern"   },
                new WordPair { wordA = "throne",   wordB = "rope"      },
                new WordPair { wordA = "ladder",   wordB = "drum"      },
                new WordPair { wordA = "chest",    wordB = "bread"     },
                new WordPair { wordA = "cart",     wordB = "barrel"    },
                new WordPair { wordA = "cage",     wordB = "flower"    },
                new WordPair { wordA = "bucket",   wordB = "rope"      },
                new WordPair { wordA = "basket",   wordB = "torch"     },
                new WordPair { wordA = "cauldron", wordB = "apple"     },

                new WordPair { wordA = "wine",     wordB = "crown"     },
                new WordPair { wordA = "cup",      wordB = "sword"     },
                new WordPair { wordA = "apple",    wordB = "shield"    },
                new WordPair { wordA = "egg",      wordB = "basket"    },
                new WordPair { wordA = "bowl",     wordB = "torch"     },
                new WordPair { wordA = "cloak",    wordB = "crown"     },
                new WordPair { wordA = "mask",     wordB = "bell"      },
                new WordPair { wordA = "helmet",   wordB = "flower"    },
                new WordPair { wordA = "bell",     wordB = "chain"     },
                new WordPair { wordA = "mirror",   wordB = "shield"    },

                new WordPair { wordA = "cannon",   wordB = "nest"      },
                new WordPair { wordA = "wheel",    wordB = "bucket"    },
                new WordPair { wordA = "bone",     wordB = "cage"      },
                new WordPair { wordA = "mushroom", wordB = "lantern"   },
                new WordPair { wordA = "anchor",   wordB = "bell"      },
                new WordPair { wordA = "anvil",    wordB = "flower"    },
                new WordPair { wordA = "saddle",   wordB = "crown"     },
                new WordPair { wordA = "barrel",   wordB = "rope"      },
                new WordPair { wordA = "tent",     wordB = "bell"      },
                new WordPair { wordA = "boat",     wordB = "cloak"     },

                new WordPair { wordA = "dog",      wordB = "chest"     },
                new WordPair { wordA = "horse",    wordB = "lantern"   },
                new WordPair { wordA = "fish",     wordB = "torch"     },
                new WordPair { wordA = "bear",     wordB = "cage"      },
                new WordPair { wordA = "owl",      wordB = "drum"      },
                new WordPair { wordA = "snake",    wordB = "bell"      },
                new WordPair { wordA = "crow",     wordB = "mask"      },
                new WordPair { wordA = "wolf",     wordB = "shield"    },
                new WordPair { wordA = "spear",    wordB = "cage"      },
                new WordPair { wordA = "hammer",   wordB = "bucket"    },
                new WordPair { wordA = "knife",    wordB = "cup"       },
                new WordPair { wordA = "staff",    wordB = "helmet"    },
            };

            var so = new SerializedObject(pool);
            var pairsProperty = so.FindProperty("pairs");
            pairsProperty.arraySize = pairs.Length;
            for (int i = 0; i < pairs.Length; i++)
            {
                var element = pairsProperty.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("wordA").stringValue = pairs[i].wordA;
                element.FindPropertyRelative("wordB").stringValue = pairs[i].wordB;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            const string path = "Assets/ScriptableObjects/WordPairPool.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(pool, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = pool;

            Debug.Log($"[WordPairPoolPopulator] Created WordPairPool with {pairs.Length} pairs at {path}");
        }
    }
}
