namespace LXLPCL.Core;

/// <summary>
/// 常用模组的中文译名（对应 Modrinth 英文标题），用于在下载列表显示中文名并支持中文搜索。
/// </summary>
public static class ModNames
{
    private static readonly Dictionary<string, string> ChineseByEnglish = new(StringComparer.OrdinalIgnoreCase)
    {
        // 性能优化
        ["Sodium"] = "钠",
        ["Lithium"] = "锂",
        ["Iris Shaders"] = "光影",
        ["Oculus"] = "光影（Forge）",
        ["Rubidium"] = "铷",
        ["Embeddium"] = "内嵌式优化",
        ["Entity Culling"] = "实体剔除优化",
        ["ImmediatelyFast"] = "即时渲染加速",
        ["FerriteCore"] = "铁氧体核心",
        ["ModernFix"] = "现代修复",
        ["Canary"] = "金丝雀优化",
        ["Krypton"] = "氪网络优化",
        ["LazyDFU"] = "延迟数据修复",
        ["Memory Leak Fix"] = "内存泄漏修复",
        ["BadOptimizations"] = "劣质优化修复",
        ["More Culling"] = "更多剔除",
        ["Dynamic FPS"] = "动态帧率",
        ["Clumps"] = "经验球合并",
        ["Starlight"] = "星光照明优化",
        ["Phosphor"] = "磷光照明优化",
        ["Nvidium"] = "英伟达渲染加速",
        ["Sodium Extra"] = "钠 · 附加选项",
        ["Indium"] = "铟渲染兼容",
        ["Reese's Sodium Options"] = "钠 · 设置界面",

        // 前置与基础库
        ["Cloth Config API"] = "布料配置 API",
        ["Mod Menu"] = "模组菜单",
        ["Yet Another Config Lib"] = "又一个配置库",
        ["Architectury API"] = "跨平台 API",
        ["Forge Config API Port"] = "配置 API 移植",
        ["Puzzles Lib"] = "谜题库",
        ["GeckoLib"] = "壁虎动画库",
        ["Bookshelf"] = "书架前置库",
        ["Balm"] = "香脂前置库",
        ["Curios API"] = "饰品栏 API",
        ["Trinkets"] = "饰品",
        ["Artifacts"] = "神器",
        ["Attribute Fix"] = "属性修复",
        ["Max Health Fix"] = "生命上限修复",

        // 界面与信息
        ["Just Enough Items"] = "物品管理器",
        ["Roughly Enough Items"] = "物品管理器（REI）",
        ["EMI"] = "物品管理器（EMI）",
        ["Just Enough Resources"] = "资源信息",
        ["AppleSkin"] = "苹果皮",
        ["Jade"] = "玉（方块信息）",
        ["WTHIT"] = "这是什么",
        ["JourneyMap"] = "旅行地图",
        ["Xaero's Minimap"] = "Xaero 小地图",
        ["Xaero's World Map"] = "Xaero 世界地图",
        ["Better Advancements"] = "更好的进度界面",
        ["Controlling"] = "按键冲突管理",
        ["Catalogue"] = "模组目录",
        ["Mouse Tweaks"] = "鼠标手势",
        ["Inventory Profiles Next"] = "背包配置",
        ["Item Highlighter"] = "物品高亮",
        ["Enchantment Descriptions"] = "附魔描述",
        ["BetterF3"] = "更好的 F3",
        ["Detail Armor Bar"] = "详细护甲条",
        ["Neat"] = "整洁血量条",
        ["Durability Viewer"] = "耐久显示",
        ["Toast Control"] = "提示控制",

        // 世界生成与结构
        ["Terralith"] = "泰拉瑞斯地形",
        ["Biomes O' Plenty"] = "超多生物群系",
        ["Oh The Biomes You'll Go"] = "你将去的生物群系",
        ["Tectonic"] = "地壳地形",
        ["Continents"] = "大陆",
        ["YUNG's Better Dungeons"] = "更好的地牢",
        ["YUNG's Better Mineshafts"] = "更好的矿井",
        ["YUNG's Better Strongholds"] = "更好的要塞",
        ["When Dungeons Arise"] = "地牢浮现之时",
        ["Structory"] = "结构点缀",
        ["Towns and Towers"] = "城镇与高塔",
        ["Repurposed Structures"] = "重构结构",
        ["Dungeons and Taverns"] = "地牢与酒馆",
        ["Incendium"] = "炼狱",
        ["Nullscape"] = "虚空之境",
        ["Amplified Nether"] = "放大下界",
        ["Better Nether"] = "更好的下界",
        ["Better End"] = "更好的末地",

        // 生物
        ["Alex's Mobs"] = "亚历克斯的生物",
        ["Alex's Caves"] = "亚历克斯的洞穴",
        ["Mowzie's Mobs"] = "莫氏生物",
        ["Ice and Fire"] = "冰与火",
        ["Cobblemon"] = "宝可梦",
        ["Aquaculture"] = "水产养殖",
        ["Productive Bees"] = "生产蜜蜂",
        ["Domestication Innovation"] = "驯化革新",

        // 科技与机械
        ["Create"] = "机械动力",
        ["Create Crafts & Additions"] = "机械动力：额外工艺",
        ["Create Deco"] = "机械动力：装饰",
        ["Create Diesel Generators"] = "机械动力：柴油发电",
        ["Farmer's Delight"] = "农夫乐事",
        ["Croptopia"] = "作物图鉴",
        ["Pam's HarvestCraft"] = "潘马斯农场",
        ["Mekanism"] = "通用机械",
        ["Applied Energistics 2"] = "应用能源 2",
        ["Refined Storage"] = "精致存储",
        ["Thermal Expansion"] = "热力膨胀",
        ["Industrial Foregoing"] = "工业先锋",
        ["Immersive Engineering"] = "沉浸工程",
        ["Powah!"] = "电力",
        ["Modern Industrialization"] = "现代工业化",
        ["Ender IO"] = "末影接口",
        ["Botany Pots"] = "植物盆栽",
        ["Mystical Agriculture"] = "神秘农业",

        // 存储
        ["Sophisticated Backpacks"] = "精致背包",
        ["Sophisticated Storage"] = "高级存储",
        ["Functional Storage"] = "功能性存储",
        ["Storage Drawers"] = "存储抽屉",
        ["Iron Chests"] = "铁箱",
        ["Tom's Simple Storage"] = "汤姆的简易存储",
        ["Travelers Backpack"] = "旅行者背包",

        // 魔法
        ["Botania"] = "植物魔法",
        ["Ars Nouveau"] = "新生魔艺",
        ["Blood Magic"] = "血魔法",
        ["Occultism"] = "神秘学",
        ["Apotheosis"] = "神化",
        ["Iron's Spells 'n Spellbooks"] = "铁之法术与法术书",
        ["Spell Engine"] = "法术引擎",
        ["Mahou Tsukai"] = "魔法使",

        // 冒险与生活
        ["Twilight Forest"] = "暮色森林",
        ["The Aether"] = "天境",
        ["Blue Skies"] = "蓝天",
        ["Epic Fight"] = "史诗战斗",
        ["Better Combat"] = "更好的战斗",
        ["Spartan Weaponry"] = "斯巴达武器",
        ["Simply Swords"] = "简易剑",
        ["Waystones"] = "传送石碑",
        ["Comforts"] = "舒适",
        ["Serene Seasons"] = "静谧季节",
        ["Fabric Seasons"] = "季节",
        ["Paraglider"] = "滑翔伞",
        ["Corpse"] = "尸体",
        ["Gravestone"] = "墓碑",
        ["Healing Campfire"] = "治疗营火",
        ["Right Click Harvest"] = "右键收获",

        // 语音与社交
        ["Simple Voice Chat"] = "简单语音",
        ["Plasmo Voice"] = "语音通话",
        ["Chat Heads"] = "聊天头像",
        ["Emotecraft"] = "表情动作",

        // 整合包常见模组
        ["Accelerated Decay"] = "加速腐化",
        ["Advanced Peripherals"] = "高级外设",
        ["AE2 Import Export Card"] = "AE2 输入输出卡",
        ["AE2 JEI Integration"] = "AE2 JEI 集成",
        ["AE2NetworkAnalyzer"] = "AE2 网络分析仪",
        ["Actually Additions"] = "实用拓展",
        ["Ad Astra"] = "星际",
        ["Additional Lights"] = "更多光源",
        ["Advanced AE"] = "高级 AE",
        ["AE2 Crafting Tree"] = "AE2 合成树",
        ["AI-Improvements"] = "AI 改进",
        ["All The Leaks"] = "泄漏修复",
        ["All the Wizard Gear"] = "全法师装备",
        ["Ars Elemental"] = "元素魔法",
        ["Ars Technica"] = "魔法科技",
        ["Applied Botanics"] = "应用植物学",
        ["Bamboo Blocks"] = "竹块",
        ["Better Compatibility Checker"] = "更好的兼容性检查",
        ["Better Villages"] = "更好的村庄",
        ["Cable Facades"] = "线缆伪装",
        ["CC: Tweaked"] = "电脑（CC）",
        ["Chipped"] = "凿子",
        ["CoFH Core"] = "CoFH 核心",
        ["Cooking for Blockheads"] = "方块头烹饪",
        ["Create: Steam 'n' Rails"] = "机械动力：蒸汽与轨道",
        ["Cucumber"] = "黄瓜前置库",
        ["Deep Resonance"] = "深层共振",
        ["Easy Villagers"] = "简易村民",
        ["Ender Storage"] = "末影存储",
        ["Extreme Reactors"] = "极限反应堆",
        ["FTB Library"] = "FTB 前置库",
        ["FTB Quests"] = "FTB 任务",
        ["FTB Teams"] = "FTB 队伍",
        ["GuideME"] = "指南书",
        ["Iron Jetpacks"] = "铁喷气背包",
        ["Just Dire Things"] = "实用物品",
        ["LaserIO"] = "激光物流",
        ["Mekanism Generators"] = "通用机械：发电机",
        ["Mekanism Tools"] = "通用机械：工具",
        ["Mekanism Additions"] = "通用机械：附加",
        ["Moonlight Lib"] = "月光前置库",
        ["Patchouli"] = "帕秋莉手册",
        ["Placebo"] = "安慰剂前置库",
        ["PneumaticCraft"] = "气动工艺",
        ["ProjectE"] = "等价交换",
        ["RFTools Base"] = "RF 工具：基础",
        ["Silent Gear"] = "寂静装备",
        ["Simply Jetpacks"] = "简易喷气背包",
        ["Sophisticated Core"] = "精致核心",
        ["Supplementaries"] = "补充",
        ["Tempad"] = "时空平板",
        ["TerraBlender"] = "地形混合",
        ["Titanium"] = "钛前置库",
        ["YUNG's API"] = "YUNG 前置库",
        ["ZeroCore2"] = "零核心 2",
        ["Zeta"] = "Zeta 前置库"
    };

    private static readonly Dictionary<string, string> EnglishByChinese = BuildReverse();

    private static Dictionary<string, string> BuildReverse()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in ChineseByEnglish) map.TryAdd(pair.Value, pair.Key);
        return map;
    }

    /// <summary>
    /// 返回模组的中文译名，没有收录时返回空字符串。
    /// fuzzy 为 true 时会做包含匹配，用于本地模组元数据里带后缀/副标题的名称。
    /// </summary>
    public static string ChineseFor(string title, bool fuzzy = false)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        var key = title.Trim();
        if (ChineseByEnglish.TryGetValue(key, out var chinese)) return chinese;
        if (!fuzzy || key.Length < 4) return "";
        key = Normalize(key);

        var matches = ChineseByEnglish
            .Where(pair => pair.Key.Length >= 4 && Normalize(pair.Key).Contains(key, StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Key.Length)
            .ToList();
        if (matches.Count > 0) return matches[0].Value;

        matches = ChineseByEnglish
            .Where(pair => pair.Key.Length >= 4 && key.Contains(Normalize(pair.Key), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(pair => pair.Key.Length)
            .ToList();
        return matches.Count > 0 ? matches[0].Value : "";
    }

    private static string Normalize(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            var ch = c switch
            {
                ':' or '：' or '-' or '_' or '—' => ' ',
                _ => c
            };
            if (ch == ' ' && builder.Length > 0 && builder[^1] == ' ') continue;
            builder.Append(ch);
        }
        return builder.ToString().Trim();
    }

    /// <summary>把中文搜索词转换成 Modrinth 能识别的英文标题；不是中文模组名时返回原词。</summary>
    public static string TranslateQuery(string query)
    {
        var text = query.Trim();
        if (text.Length == 0) return query;
        if (text.All(c => c < 128)) return query;
        if (EnglishByChinese.TryGetValue(text, out var english)) return english;

        var candidates = ChineseByEnglish
            .Where(pair => pair.Value.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Value.Length)
            .ToList();
        return candidates.Count > 0 ? candidates[0].Key : query;
    }
}
