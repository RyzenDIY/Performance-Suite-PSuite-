// language: C#, file: UI/BrowserTab.cs
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

public class PresetCategory
{
    public string Name;
    public string NameRu;
    public string[] Prefixes;
    public bool GameplayOnly;

    public PresetCategory(string name, string nameRu, string[] prefixes, bool gameplayOnly = false)
    {
        Name = name; NameRu = nameRu; Prefixes = prefixes; GameplayOnly = gameplayOnly;
    }

    public override string ToString() => $"{Name}   ·   {NameRu}";
}

public class BrowserTab : UserControl
{
    static readonly PresetCategory[] Categories = new[]
    {
        new PresetCategory("All classes",           "всі класи",           null),
        new PresetCategory("Gameplay only",         "тільки ігрові",       null, gameplayOnly: true),
        new PresetCategory("Player",                "гравець",             new[] {
            "BasePlayer", "LocalPlayer", "PlayerInventory", "PlayerEyes",
            "PlayerWalkMovement", "PlayerModel", "PlayerBelt", "PlayerTeam",
            "PlayerMetabolism", "PlayerCorpse", "PlayerVoiceSpeaker", "PlayerNameTag"
        }),
        new PresetCategory("Player movement",       "рух гравця",          new[] {
            "PlayerWalkMovement", "BaseMovement", "ModelState", "PlayerModel", "PlayerTick"
        }),
        new PresetCategory("Player inventory",      "інвентар гравця",     new[] {
            "PlayerInventory", "ItemContainer", "Item", "ItemDefinition",
            "PlayerBelt", "ItemMod", "ItemBlueprint"
        }),
        new PresetCategory("Weapons / Projectile",  "зброя / снаряди",     new[] {
            "BaseProjectile", "AttackEntity", "BaseMelee", "HeldEntity",
            "Magazine", "BaseLauncher", "RecoilProperties", "ServerProjectile", "Projectile"
        }),
        new PresetCategory("Items / Loot",          "предмети / лут",      new[] {
            "Item", "ItemDefinition", "ItemMod", "ItemContainer",
            "ItemBlueprint", "LootContainer", "SupplyDrop", "HackableLockedCrate"
        }),
        new PresetCategory("Entities",              "сутності",            new[] {
            "BaseEntity", "BaseNetworkable", "BaseCombatEntity",
            "BasePlayer", "BaseCorpse", "BaseVehicle", "BaseResourceEntity"
        }),
        new PresetCategory("NPC / Animals",         "NPC / тварини",       new[] {
            "BaseAnimalNPC", "Bear", "Wolf", "Boar", "Stag", "Chicken", "Horse",
            "PolarBear", "Snake", "Shark", "Corn",
            "ScientistNPC", "TunnelDweller", "ScarecrowNPC", "Bandit", "UnderwaterDweller"
        }),
        new PresetCategory("Buildings",             "будівлі",             new[] {
            "BuildingBlock", "BuildingPrivlidge", "Door", "BaseOven",
            "Furnace", "Campfire", "AutoTurret", "Recycler", "RepairBench",
            "ResearchTable", "VendingMachine", "SleepingBag", "Bed"
        }),
        new PresetCategory("Containers",            "контейнери",          new[] {
            "LootContainer", "BoxStorage", "StashContainer",
            "ToolCupboard", "LootableCorpse"
        }),
        new PresetCategory("Vehicles",              "транспорт",           new[] {
            "BaseVehicle", "MiniCopter", "ScrapTransportHelicopter",
            "ModularCar", "MotorRowboat", "RHIB", "Submarine", "Bike",
            "CargoShip", "PatrolHelicopter", "BradleyAPC"
        }),
        new PresetCategory("Camera / Rendering",    "камера / рендер",     new[] {
            "Camera", "MainCamera", "CameraController",
            "Renderer", "SkinnedMeshRenderer", "MeshRenderer", "MeshFilter"
        }),
        new PresetCategory("World / Terrain",       "світ / ландшафт",     new[] {
            "Terrain", "World", "WorldSetup", "WorldItem", "Prefab", "GameObject"
        }),
        new PresetCategory("Team / Group",          "команда / група",     new[] {
            "PlayerTeam", "TeamManager", "RelationshipManager"
        }),
        new PresetCategory("Network",               "мережа",              new[] {
            "BaseNetworkable", "Networkable", "NetworkPeer", "Connection"
        }),
        new PresetCategory("Sound / Audio",         "звук",                new[] {
            "Sound", "AudioSource", "SoundDefinition"
        }),
        new PresetCategory("Physics",               "фізика",              new[] {
            "Rigidbody", "Collider", "BoxCollider", "SphereCollider",
            "CapsuleCollider", "CharacterController"
        }),
        new PresetCategory("UI / Hud",              "інтерфейс / HUD",     new[] {
            "Canvas", "Hud", "Crosshair", "HitMarker", "UI_", "UIBlackout"
        }),
        new PresetCategory("▶ BasePlayer only",     "тільки BasePlayer",   new[] { "BasePlayer" }),
        new PresetCategory("▶ Transform only",      "тільки Transform",    new[] { "Transform" }),
        new PresetCategory("▶ Camera only",         "тільки Camera",       new[] { "Camera" }),
        new PresetCategory("▶ MainCamera only",     "тільки MainCamera",   new[] { "MainCamera" }),
        new PresetCategory("▶ BaseNetworkable only","тільки BaseNetworkable", new[] { "BaseNetworkable" }),
        new PresetCategory("▶ BaseEntity only",     "тільки BaseEntity",   new[] { "BaseEntity" }),
    };

    static readonly string[] GameplayPrefixes = {
        "Base", "Player", "LocalPlayer", "Item", "Building", "Vehicle",
        "World", "Terrain", "Animal", "Bear", "Wolf", "Boar", "Stag",
        "Chicken", "Horse", "PolarBear", "Snake", "Shark",
        "Door", "Box", "Stash", "Tool", "Furnace", "Campfire", "Oven",
        "Camera", "Sound", "Held", "Attack", "Magazine", "Projectile",
        "Scientist", "Tunnel", "Scarecrow", "Bandit", "Recoil", "Crosshair",
        "Hit", "Hud", "UI_", "Team", "Relationship",
        "Loot", "Supply", "Hackable", "AutoTurret", "Bradley", "CargoShip",
        "PatrolHelicopter", "Grenade", "Flare", "Rocket", "Signal",
        "SleepingBag", "Bed", "VendingMachine", "Marketplace", "Recycler",
        "RepairBench", "ResearchTable", "Workbench", "Planning",
        "ServerProjectile", "ServerGib", "Gib", "Effect",
        "Trigger", "ResourceEntity", "Tree", "Ore", "Mining", "Fishing",
        "Crafting", "Quest", "Mission", "Clan", "Ridable",
    };

    static readonly Dictionary<string, string> ClassDescriptions = new(StringComparer.Ordinal)
    {
        { "BasePlayer",           "Клас гравця. HP, ім'я, команда, флаги, інвентар." },
        { "LocalPlayer",          "Статичне посилання на поточного гравця." },
        { "BaseEntity",           "База всіх сутностей у світі." },
        { "BaseNetworkable",      "Список ВСІХ завантажених сутностей. Для ESP." },
        { "BaseCombatEntity",     "Все що має HP." },
        { "PlayerWalkMovement",   "Фізика руху. Для fly / noclip / speedhack." },
        { "PlayerEyes",           "Куди дивиться гравець. Для aimbot." },
        { "PlayerInventory",      "Інвентар гравця." },
        { "PlayerModel",          "Модель гравця. Для skeleton ESP." },
        { "PlayerTeam",           "Команда гравця." },
        { "Transform",            "Позиція/обертання. Для W2S." },
        { "Camera",               "Камера. View matrix." },
        { "MainCamera",           "Головна камера гравця." },
        { "BaseProjectile",       "Зброя. recoilScale / aimCone / aimSway." },
        { "AttackEntity",         "Атакуючі (зброя, гранати)." },
        { "BaseMelee",            "Ближня зброя." },
        { "HeldEntity",           "Те що тримає гравець." },
        { "Item",                 "Предмет в інвентарі." },
        { "ItemContainer",        "Контейнер." },
        { "ItemDefinition",       "Визначення предмету." },
        { "Magazine",             "Магазин зброї." },
        { "BaseAnimalNPC",        "Тварина." },
        { "Bear",                 "Ведмідь." },
        { "Wolf",                 "Вовк." },
        { "BuildingBlock",        "Будівельний блок." },
        { "ToolCupboard",         "Шафа (TC)." },
        { "LootContainer",        "Ящик з лутом." },
        { "SupplyDrop",           "Аірдроп." },
        { "HackableLockedCrate",  "Хакований ящик." },
        { "BaseVehicle",          "Транспорт." },
        { "MiniCopter",           "Мінікоптер." },
        { "BradleyAPC",           "Танк Bradley." },
        { "PatrolHelicopter",     "Патрульний вертоліт." },
        { "Sound",                "Звук." },
        { "Rigidbody",            "Фізичне тіло." },
        { "Collider",             "Колізія." },
        { "Renderer",             "Рендерер." },
        { "Terrain",              "Ландшафт." },
        { "Hud",                  "HUD." },
        { "Crosshair",            "Приціл." },
        { "AutoTurret",           "Автотуррель." },
        { "Furnace",              "Печі." },
        { "Door",                 "Двері." },
    };

    static readonly HashSet<string> PrimitiveTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.Int32", "System.UInt32", "System.Int16", "System.UInt16",
        "System.Int64", "System.UInt64", "System.Byte", "System.SByte",
        "System.Single", "System.Double", "System.Boolean", "System.Char",
        "System.String",
        "UnityEngine.Vector2", "UnityEngine.Vector3", "UnityEngine.Vector4",
        "UnityEngine.Quaternion", "UnityEngine.Color", "UnityEngine.Color32",
        "UnityEngine.Rect", "UnityEngine.Bounds",
    };

    // controls
    TextBox _filter;
    ComboBox _categoryBox;
    Label _count, _fieldInfo, _summary;
    TreeView _tree;
    Button _markBtn;
    Panel _rightPanel;
    System.Windows.Forms.Timer _filterTimer;

    bool _stHideObf, _stHideStatic, _stHideReadonly, _stOnlyPrim, _stHideGen;
    PresetCategory _category;
    volatile bool _populating;

    string _currentClassName;
    string _currentFieldName;
    int _currentFieldOffset;
    string _currentFieldType;
    bool _currentFieldStatic;

    static void L(string s)
    {
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "ui-error.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " [browser] " + s + "\n"); }
        catch { }
    }

    public BrowserTab()
    {
        try
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Bg;
            DoubleBuffered = true;

            // ═══════ body (Fill): tree + right panel
            var bodyPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };

            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.LogBg,
                ForeColor = Theme.Text,
                Font = new Font("Consolas", 9.5f),
                BorderStyle = BorderStyle.None,
                HideSelection = false,
                LineColor = Theme.Line,
                ShowLines = true,
                ShowPlusMinus = true,
                ShowRootLines = true,
                Scrollable = true,
                ItemHeight = 20,
                Indent = 18
            };
            _tree.AfterSelect += (_, e) => ShowField(e.Node);
            _tree.NodeMouseDoubleClick += OnNodeDoubleClick;
            _tree.BeforeExpand += OnNodeBeforeExpand;
            bodyPanel.Controls.Add(_tree);

            _rightPanel = new Panel { Dock = DockStyle.Right, Width = 400, BackColor = Theme.Card };

            _markBtn = new Button
            {
                Text = "❌  вибери поле",
                Location = new Point(14, 14),
                Size = new Size(372, 44),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(55, 25, 30),
                ForeColor = Color.FromArgb(220, 120, 120),
                Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
                Cursor = Cursors.Default,
                Enabled = true
            };
            _markBtn.FlatAppearance.BorderSize = 1;
            _markBtn.FlatAppearance.BorderColor = Color.FromArgb(120, 45, 55);
            _markBtn.Click += (_, _) => MarkSelectedField();
            _rightPanel.Controls.Add(_markBtn);

            _fieldInfo = new Label
            {
                Location = new Point(0, 72),
                Size = new Size(400, 600),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                BackColor = Color.Transparent,
                ForeColor = Theme.Text,
                Font = new Font("Consolas", 9f),
                Padding = new Padding(16),
                Text = "вибери поле"
            };
            _rightPanel.Controls.Add(_fieldInfo);
            bodyPanel.Controls.Add(_rightPanel);
            _rightPanel.BringToFront();

            // ═══════ summary
            var summaryPanel = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Theme.Card };
            _summary = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Theme.Dim,
                Font = new Font("Consolas", 9f),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                Text = "no dump loaded"
            };
            summaryPanel.Controls.Add(_summary);

            // ═══════ chips
            var chkPanel = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Theme.Panel };
            chkPanel.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(Theme.Line), 0, chkPanel.Height - 1, chkPanel.Width, chkPanel.Height - 1);
            AddToggle(chkPanel, "hide obfuscated %", 20, () => _stHideObf, v => _stHideObf = v);
            AddToggle(chkPanel, "hide static", 210, () => _stHideStatic, v => _stHideStatic = v);
            AddToggle(chkPanel, "hide readonly", 390, () => _stHideReadonly, v => _stHideReadonly = v);
            AddToggle(chkPanel, "only primitives", 570, () => _stOnlyPrim, v => _stOnlyPrim = v);
            AddToggle(chkPanel, "hide compiler-gen", 750, () => _stHideGen, v => _stHideGen = v);

            // ═══════ top: filter + category
            var topPanel = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Theme.Panel };
            topPanel.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(Theme.Line), 0, topPanel.Height - 1, topPanel.Width, topPanel.Height - 1);

            topPanel.Controls.Add(Theme.Lbl("filter:", 20, 18, true));
            _filter = Theme.Txt("", 80, 16, 280);
            _filter.TextChanged += (_, _) => DebounceFilter();
            topPanel.Controls.Add(_filter);

            topPanel.Controls.Add(Theme.Lbl("category:", 380, 18, true));
            _categoryBox = new ComboBox
            {
                Location = new Point(455, 16),
                Width = 420,
                Height = 28,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.Input,
                ForeColor = Theme.Text,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f)
            };
            _category = Categories[0];
            foreach (var c in Categories) _categoryBox.Items.Add(c);
            _categoryBox.SelectedIndex = 0;
            _categoryBox.SelectedIndexChanged += (_, _) =>
            {
                try
                {
                    _category = _categoryBox.SelectedItem as PresetCategory;
                    DebounceFilter();
                }
                catch (Exception ex) { L("combo: " + ex.Message); }
            };
            topPanel.Controls.Add(_categoryBox);

            _count = Theme.Lbl("", 890, 20, dim: true);
            topPanel.Controls.Add(_count);

            // порядок: Fill першим, Top-панелі після (останній доданий = найвищий)
            Controls.Add(bodyPanel);
            Controls.Add(summaryPanel);
            Controls.Add(chkPanel);
            Controls.Add(topPanel);

            AppState.DumpChanged += OnDumpChanged;
            OnDumpChanged();
        }
        catch (Exception ex)
        {
            L("constructor: " + ex);
            throw;
        }
    }

    // ─── debounce (300 мс після останнього символу)

    void DebounceFilter()
    {
        if (_filterTimer == null)
        {
            _filterTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _filterTimer.Tick += (_, _) =>
            {
                _filterTimer.Stop();
                Populate();
            };
        }
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    void AddToggle(Panel parent, string text, int x, Func<bool> get, Action<bool> set)
    {
        int w = 175;
        bool state = get();

        var p = new Panel
        {
            Location = new Point(x, 8),
            Size = new Size(w, 26),
            BackColor = state ? Theme.Accent : Theme.Input,
            Cursor = Cursors.Hand
        };

        p.Paint += (s, e) =>
        {
            var g = e.Graphics;
            using var bg = new SolidBrush(p.BackColor);
            g.FillRectangle(bg, 0, 0, p.Width, p.Height);

            using var border = new Pen(state ? Theme.Accent : Theme.Line);
            g.DrawRectangle(border, 0, 0, p.Width - 1, p.Height - 1);

            var cb = new Rectangle(8, 6, 14, 14);
            using var cbPen = new Pen(state ? Color.White : Theme.Dim, 1.5f);
            g.DrawRectangle(cbPen, cb);
            if (state)
            {
                using var fill = new SolidBrush(Color.White);
                g.FillRectangle(fill, cb.X + 3, cb.Y + 3, cb.Width - 6, cb.Height - 6);
            }

            using var fg = new SolidBrush(state ? Color.White : Theme.Text);
            using var font = new Font("Segoe UI", 8.5f, state ? FontStyle.Bold : FontStyle.Regular);
            g.DrawString(text, font, fg, 28, 5);
        };

        p.Click += (_, _) =>
        {
            state = !state;
            set(state);
            p.BackColor = state ? Theme.Accent : Theme.Input;
            p.Invalidate();
            Populate();
        };

        parent.Controls.Add(p);
    }

    void OnDumpChanged()
    {
        try
        {
            if (InvokeRequired) { BeginInvoke(new Action(OnDumpChanged)); return; }
            var d = AppState.CurrentDump;
            if (d == null)
            {
                if (_summary != null) _summary.Text = "no dump loaded — go to Dump tab";
                if (_tree != null) _tree.Nodes.Clear();
                if (_count != null) _count.Text = "";
                return;
            }
            if (_summary != null)
                _summary.Text = $"  game: {d.GameId}    ·    classes: {d.Classes.Count}    ·    captured: {d.CapturedAt:yyyy-MM-dd HH:mm}";
            Populate();
        }
        catch (Exception ex) { L("OnDumpChanged: " + ex.Message); }
    }

    bool PassCategory(PresetCategory cat, string className)
    {
        if (cat == null) return true;
        if (cat.Prefixes == null && !cat.GameplayOnly) return true;

        if (cat.GameplayOnly)
        {
            foreach (var p in GameplayPrefixes)
                if (className.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }

        foreach (var p in cat.Prefixes)
            if (className.Equals(p, StringComparison.Ordinal)) return true;

        return false;
    }

    bool PassField(FieldModel f)
    {
        if (_stHideObf && f.Name.StartsWith("%")) return false;
        if (_stHideStatic && f.IsStatic) return false;

        if (_stHideReadonly)
        {
            string n = f.Name ?? "";
            if (n.StartsWith("get_")) return false;
        }

        if (_stOnlyPrim)
        {
            string t = f.Type ?? "";
            int lt = t.IndexOf('<');
            if (lt >= 0) t = t.Substring(0, lt);
            t = t.Trim();

            bool ok = false;
            foreach (var p in PrimitiveTypes)
            {
                if (t.Equals(p, StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
                int dot = p.LastIndexOf('.');
                if (dot >= 0 && t.EndsWith(p.Substring(dot + 1), StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
            }
            if (!ok) return false;
        }

        if (_stHideGen)
        {
            string n = f.Name ?? "";
            if (n.Contains("<>") || n.Contains("__") || n.StartsWith("_y_") || n.Contains("CompilerGenerated")) return false;
        }

        return true;
    }

    // ─── lazy-load полів при розкритті

    void OnNodeBeforeExpand(object sender, TreeViewCancelEventArgs e)
    {
        try
        {
            if (e.Node == null) return;
            if (e.Node.Parent != null) return;
            if (!(e.Node.Tag is ClassModel cls)) return;

            // якщо вже завантажені справжні поля — виходимо
            if (e.Node.Nodes.Count > 0 && e.Node.Nodes[0].Text != "dummy") return;

            e.Node.Nodes.Clear();
            var fields = cls.Fields.Where(PassField).OrderBy(x => x.Offset).ToList();
            foreach (var f in fields)
                e.Node.Nodes.Add(FormatField(f));
        }
        catch (Exception ex) { L("BeforeExpand: " + ex.Message); }
    }

    // ─── background populate

    void Populate()
    {
        var d = AppState.CurrentDump;
        if (d == null || _tree == null) return;
        if (_populating) return;
        _populating = true;

        string filter = (_filter?.Text ?? "").Trim().ToLowerInvariant();
        var cat = _category;

        // snapshot state flags для потоку
        bool fHideObf = _stHideObf, fHideStatic = _stHideStatic,
             fHideReadonly = _stHideReadonly, fOnlyPrim = _stOnlyPrim, fHideGen = _stHideGen;

        new Thread(() =>
        {
            var batch = new List<TreeNode>();
            int shownClasses = 0;
            int totalFields = 0;
            string err = null;

            try
            {
                foreach (var kv in d.Classes.OrderBy(x => x.Key))
                {
                    var cls = kv.Value;

                    if (!PassCategory(cat, cls.Name)) continue;

                    bool classMatch = filter.Length == 0 || cls.Name.ToLowerInvariant().Contains(filter);

                    // фільтруємо поля зі збереженими станами (безпечно у фоні)
                    IEnumerable<FieldModel> baseFields = cls.Fields.Where(f => PassFieldWith(f, fHideObf, fHideStatic, fHideReadonly, fOnlyPrim, fHideGen));
                    List<FieldModel> matchingFields = (filter.Length == 0 || classMatch)
                        ? baseFields.ToList()
                        : baseFields.Where(f => f.Name.ToLowerInvariant().Contains(filter)).ToList();

                    if (!classMatch && matchingFields.Count == 0) continue;

                    string countLabel = cls.Fields.Count == matchingFields.Count
                        ? $"{cls.Fields.Count} fields"
                        : $"{matchingFields.Count}/{cls.Fields.Count} fields";

                    var node = new TreeNode($"{cls.Name}   ({countLabel})") { ForeColor = Theme.Text };
                    node.Tag = cls;

                    if (filter.Length > 0)
                    {
                        // при фільтрі — одразу розкриваємо поля
                        foreach (var f in matchingFields.OrderBy(x => x.Offset))
                            node.Nodes.Add(FormatField(f));
                        node.Expand();
                    }
                    else
                    {
                        // lazy: dummy-вузол щоб TreeView показав ►
                        if (matchingFields.Count > 0)
                            node.Nodes.Add("dummy");
                    }

                    batch.Add(node);
                    shownClasses++;
                    totalFields += matchingFields.Count;

                    if (shownClasses >= 3000) break;
                }
            }
            catch (Exception ex) { err = ex.Message; }

            try
            {
                Invoke(new Action(() =>
                {
                    try
                    {
                        _tree.BeginUpdate();
                        try
                        {
                            _tree.Nodes.Clear();
                            foreach (var n in batch) _tree.Nodes.Add(n);
                        }
                        finally { _tree.EndUpdate(); }

                        string clsTxt = shownClasses == 1 ? "1 class" : $"{shownClasses} classes";
                        string catName = cat?.Name ?? "All";
                        if (_count != null)
                            _count.Text = filter.Length == 0
                                ? $"{clsTxt}  ·  {catName}"
                                : $"{clsTxt}  ·  {totalFields} matching  ·  {catName}";
                    }
                    catch (Exception ex) { L("apply batch: " + ex.Message); }
                    finally { _populating = false; }
                }));
            }
            catch { _populating = false; }

            if (err != null) L("populate bg: " + err);
        })
        { IsBackground = true }.Start();
    }

    // потокобезпечний PassField (без звертання до полів форми)
    static bool PassFieldWith(FieldModel f, bool hideObf, bool hideStatic, bool hideReadonly, bool onlyPrim, bool hideGen)
    {
        if (hideObf && f.Name.StartsWith("%")) return false;
        if (hideStatic && f.IsStatic) return false;

        if (hideReadonly)
        {
            string n = f.Name ?? "";
            if (n.StartsWith("get_")) return false;
        }

        if (onlyPrim)
        {
            string t = f.Type ?? "";
            int lt = t.IndexOf('<');
            if (lt >= 0) t = t.Substring(0, lt);
            t = t.Trim();

            bool ok = false;
            foreach (var p in PrimitiveTypes)
            {
                if (t.Equals(p, StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
                int dot = p.LastIndexOf('.');
                if (dot >= 0 && t.EndsWith(p.Substring(dot + 1), StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
            }
            if (!ok) return false;
        }

        if (hideGen)
        {
            string n = f.Name ?? "";
            if (n.Contains("<>") || n.Contains("__") || n.StartsWith("_y_") || n.Contains("CompilerGenerated")) return false;
        }

        return true;
    }

    static string FormatField(FieldModel f)
        => $"+0x{f.Offset:X4}   {(f.IsStatic ? "static" : "inst  ")}   {f.Type,-30}  {f.Name}";

    void OnNodeDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
    {
        try
        {
            if (e.Node == null) return;
            if (e.Node.Parent != null)
            {
                try { Clipboard.SetText(e.Node.Text); } catch { }
                return;
            }
            e.Node.Toggle();
        }
        catch (Exception ex) { L("DoubleClick: " + ex.Message); }
    }

    void ShowField(TreeNode node)
    {
        try
        {
            if (node == null)
            {
                _fieldInfo.Text = "Нічого не вибрано.\r\nКлікни на клас → розкрий ► → клікни на поле.";
                _currentFieldName = null;
                SetMarkBtnActive(false, "вибери поле");
                return;
            }

            if (node.Parent == null)
            {
                var cls = node.Tag as ClassModel;
                if (cls == null)
                {
                    _fieldInfo.Text = node.Text;
                    _currentFieldName = null;
                    SetMarkBtnActive(false, "вибери поле");
                    return;
                }

                int instanceCount = cls.Fields.Count(f => !f.IsStatic);
                int visible = cls.Fields.Count(PassField);
                string descr = ClassDescriptions.TryGetValue(cls.Name, out var dd) ? dd : "—";

                _fieldInfo.Text =
                    $"КЛАС\r\n──────────────────\r\n\r\n" +
                    $"  {cls.Name}\r\n\r\n" +
                    $"  Parent:     {cls.Parent ?? "—"}\r\n" +
                    $"  Fields:     {cls.Fields.Count}\r\n" +
                    $"  instance:   {instanceCount}\r\n" +
                    $"  показано:   {visible}\r\n\r\n" +
                    $"Що це:\r\n  {descr}\r\n\r\n" +
                    $"Щоб розмітити поле:\r\n  1. розкрий ►\r\n  2. клікни на поле\r\n  3. тисни кнопку зверху";
                _currentFieldName = null;
                SetMarkBtnActive(false, "спочатку розкрий клас ►");
                return;
            }

            // поле
            string line = node.Text;
            string clsName = node.Parent.Text.Split('(')[0].Trim();
            var parentCls = AppState.CurrentDump?.GetClass(clsName);
            FieldModel fld = null;
            if (parentCls != null)
                fld = parentCls.Fields.FirstOrDefault(f => FormatField(f) == line);

            if (fld != null)
            {
                _currentClassName = clsName;
                _currentFieldName = fld.Name;
                _currentFieldOffset = fld.Offset;
                _currentFieldType = fld.Type;
                _currentFieldStatic = fld.IsStatic;

                bool sus = fld.IsStatic && fld.Offset == 0;
                SetMarkBtnActive(true, sus ? "⚠ static @0x0 — може не потрібне" : null);
            }
            else
            {
                _currentFieldName = null;
                SetMarkBtnActive(false, "вибери поле");
            }

            string clsHint = ClassDescriptions.TryGetValue(clsName, out var h) ? h : null;

            _fieldInfo.Text =
                $"ПОЛЕ\r\n──────────────────\r\n\r\n" +
                $"  {fld?.Name ?? "—"}\r\n\r\n" +
                $"  Class:      {clsName}\r\n" +
                $"  Offset:     0x{fld?.Offset ?? 0:X}\r\n" +
                $"  Type:       {fld?.Type ?? "—"}\r\n" +
                $"  Static:     {(fld?.IsStatic == true ? "yes" : "no")}\r\n" +
                $"  Instance:   {(fld?.IsStatic == false ? "yes" : "no")}\r\n\r\n" +
                (clsHint != null ? $"Що це клас:\r\n  {clsHint}\r\n\r\n" : "") +
                $"Дії:\r\n" +
                $"  • права кнопка миші → Mark as…\r\n" +
                $"  • кнопка зверху → Mark as…\r\n" +
                $"  • подвійний клік → скопіювати";
        }
        catch (Exception ex) { L("ShowField: " + ex.Message); }
    }

    void SetMarkBtnActive(bool active, string warn = null)
    {
        if (_markBtn == null) return;

        if (active && warn == null)
        {
            _markBtn.Text = "➕  Mark this field as…";
            _markBtn.BackColor = Color.FromArgb(180, 40, 55);
            _markBtn.ForeColor = Color.White;
            _markBtn.FlatAppearance.BorderColor = Color.FromArgb(240, 60, 75);
            _markBtn.Cursor = Cursors.Hand;
        }
        else if (active && warn != null)
        {
            _markBtn.Text = "⚠  " + warn;
            _markBtn.BackColor = Color.FromArgb(90, 65, 20);
            _markBtn.ForeColor = Color.FromArgb(240, 200, 120);
            _markBtn.FlatAppearance.BorderColor = Color.FromArgb(180, 130, 50);
            _markBtn.Cursor = Cursors.Hand;
        }
        else
        {
            _markBtn.Text = "❌  " + (warn ?? "вибери поле");
            _markBtn.BackColor = Color.FromArgb(55, 25, 30);
            _markBtn.ForeColor = Color.FromArgb(220, 120, 120);
            _markBtn.FlatAppearance.BorderColor = Color.FromArgb(120, 45, 55);
            _markBtn.Cursor = Cursors.Default;
        }
        _markBtn.Invalidate();
    }

    void MarkSelectedField()
    {
        try
        {
            if (AppState.ActiveGame == null)
            {
                MessageBox.Show("Спочатку вибери гру на табі Games.");
                return;
            }
            if (string.IsNullOrEmpty(_currentFieldName))
            {
                MessageBox.Show("Спочатку клікни на поле у дереві (не на клас).");
                return;
            }

            using var dlg = new MarkAsDialog(_currentClassName, _currentFieldName,
                                              _currentFieldOffset, _currentFieldType, _currentFieldStatic);
            if (dlg.ShowDialog() != DialogResult.OK) return;

            LabelService.Add(AppState.ActiveGame.Id, dlg.Semantic,
                             _currentClassName, _currentFieldName,
                             _currentFieldOffset, _currentFieldType,
                             _currentFieldStatic, dlg.Description);

            MessageBox.Show($"✓ '{dlg.Semantic}' ← {_currentClassName}.{_currentFieldName} @ +0x{_currentFieldOffset:X}\n\n" +
                            "Дивись таб Labels.");
        }
        catch (Exception ex) { L("Mark: " + ex); }
    }
}