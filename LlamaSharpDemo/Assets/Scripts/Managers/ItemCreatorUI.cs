using UnityEngine;
using UnityEngine.UI;

public class ItemCreatorUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The root panel of the item creator window (should have opaque background)")]
    public GameObject windowPanel;
    public InputField nameInput;
    public InputField descriptionInput;
    public Button generateButton;
    public Button presetButton;
    public Button toggleButton;

    [Header("Scene References")]
    public ItemSpawner itemSpawner;
    public PlayerController playerController;

    private bool isOpen = false;

    private int currentPresetIndex = -1;
    private readonly (string name, string desc)[] presets = new (string, string)[]
    {
        ("Phoenix Wing Coat", "Embrace the essence of the undying bird. Soar through the skies with grace and let the flames of rebirth mend your wounds instantly."),
        ("Titan's Seismic Gauntlet", "Channel the earth's fury. Strike with heavy force to shatter the ground beneath, while a protective aura hardens your skin against retaliation."),
        ("Voidwalker's Prism", "Step into the void to vanish and reappear instantly forward. Upon exit, time distorts around you, making enemies sluggish and heavy."),
        ("Berzerker's Bloodlust Drug", "A forbidden stimulant that ignores pain. Charge recklessly into the fray with blinding speed, shrugging off injuries as if you were immortal for a brief moment."),
        ("Stormcaller's Rod", "Summon the tempest. Launch a bolt of raw lightning that pierces through lines of foes, leaving them shocked and stunned in its wake."),
        ("Ninja's Smoke Bomb", "A tactical escape tool. Throw a bomb that explodes to cover your retreat, while simultaneously propelling you upwards to a vantage point."),
        ("Cryo-Stasis Field Generator", "Deploys a field of absolute zero. Enemies caught within are lifted helplessly into the frozen air, their movements grinding to a halt."),
        ("Vanguard's Charge Shield", "Raise a barrier of light to deflect harm and rush forward to slam into enemy lines, knocking them senseless."),
        ("Dragon's Breath Elixir", "Drink the liquid fire. Spew forth a devastating fireball that detonates on impact, while your inner heat accelerates your recovery."),
        ("Shadow Assassin's Cloak", "Become one with the shadows. Teleport behind your target and unleash a swift, lethal strike before they even know you're there.")
    };

    void Start()
    {
        // Auto-find references if missing
        if (itemSpawner == null) itemSpawner = FindFirstObjectByType<ItemSpawner>();
        if (playerController == null) playerController = FindFirstObjectByType<PlayerController>();

        // Setup Button Listeners
        if (generateButton != null)
        {
            generateButton.onClick.AddListener(OnGenerateClicked);
        }

        if (toggleButton != null)
        {
            toggleButton.onClick.AddListener(ToggleWindow);
        }

        if (presetButton != null)
        {
            presetButton.onClick.AddListener(CyclePreset);
        }

        // Initialize state
        if (windowPanel != null)
        {
            windowPanel.SetActive(false);
        }
        isOpen = false;
    }

    void Update()
    {
        // Optional: Close with Escape
        if (isOpen && DemoInput.GetKeyDown(KeyCode.Escape))
        {
            ToggleWindow();
        }
    }

    public void ToggleWindow()
    {
        isOpen = !isOpen;

        if (windowPanel != null)
        {
            windowPanel.SetActive(isOpen);
        }

        // Block/Unblock Player Input
        if (playerController != null)
        {
            playerController.SetInputEnabled(!isOpen);
        }
    }

    public void CyclePreset()
    {
        currentPresetIndex = (currentPresetIndex + 1) % presets.Length;
        var (name, desc) = presets[currentPresetIndex];

        if (nameInput != null) nameInput.text = name;
        if (descriptionInput != null) descriptionInput.text = desc;

        Debug.Log($"[ItemCreatorUI] Loaded Preset: {name}");
    }

    public void OnGenerateClicked()
    {
        if (nameInput == null || descriptionInput == null) return;

        string name = nameInput.text;
        string desc = descriptionInput.text;

        if (string.IsNullOrWhiteSpace(name))
        {
            Debug.LogWarning("[ItemCreatorUI] Name is empty!");
            return;
        }

        if (itemSpawner != null)
        {
            itemSpawner.SpawnCustomItem(name, desc);
        }

        // Keep inputs for now unless cleared
        // nameInput.text = "";
        // descriptionInput.text = "";
    }
}
