# NexusAnimaDemo — 场景隔离

两张可玩场景必须单独编辑，禁止互相污染。

| 文件 | 内容 |
|---|---|
| `Assets/Scenes/BattleDemo.unity` | 演唱会（PBR Stage） |
| `Assets/Scenes/BattleDemo_AncientChineseArena.unity` | 古代中国 |

AI / Unity MCP 规则（细节见 `~/.grok/skills/unityscenes/SKILL.md`）：

1. 先读当前 `GetActiveScene().name`。和用户说的不一致就停，不要擅自 `OpenScene`。
2. `sceneCount` 必须为 1。禁止 Additive。
3. 只 `SaveScene(active)`。禁止 `SaveOpenScenes`。
4. 禁止 Prefab Apply；材质只改实例 `renderer.materials`。
5. 古代中国不要写演唱会相机/棋盘坐标；反过来也不要。
6. 不要动 Scene View 相机，除非用户说飞丢了。

古代中国黄金备份（2026-09-09）：

- Unity：`Assets/_Recovery/AncientChina_Golden/BattleDemo_AncientChineseArena.unity`
- 工程外只读：`_Backups/AncientChina/BattleDemo_AncientChineseArena_20260909.unity`
- 还原说明：`Assets/_Recovery/AncientChina_Golden/HOW_TO_RESTORE.md`

回滚时只覆盖工作场景 `.unity` 内容，保留 `.meta`。不要打开备份图来编辑。
