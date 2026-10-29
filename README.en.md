# MakoMaster

Automatic typing, repeat play, and chest utilities for 魔光键影 on the mainland Chinese version of Final Fantasy XIV.

[简体中文](README.md)

## Installation

Add this URL under **Custom Plugin Repositories** in Dalamud settings, save, refresh the plugin list, and install **MakoMaster**:

```text
https://raw.githubusercontent.com/DocherPap/MakoMaster/main/repo.json
```

## Getting started

1. Enter `/makomaster` to open the window.
2. Select a mode, enter a game count or chest quantity, and click **Start (开始)**.
3. **Stop (停止)** ends the current operation, including typing, travel, navigation, and exchange.

| Mode | Behavior | Initial preset |
| --- | --- | --- |
| Solo (单人排本) | Register, accept departure, type, and exit results. Stop after the requested game count. | Default |
| Party leader (组队队长) | Requires a four-person party and leadership. Register and complete the requested game count automatically. | Fast |
| Party member (组队队员) | Requires a four-person party and non-leader status. Wait for the leader, accept departure, type, and exit results until stopped manually. | Fast |
| Open chests (开启宝箱) | Open the requested quantity of 魔光键影宝箱, stopping earlier if your regular inventory runs out. | — |

- Game counts accept **0–9999**; **0 means unlimited games**. A chest quantity of **0** opens until no matching chests remain in regular inventory bags.
- One complete results screen counts as one game; individual waves do not. Starting during a game resumes that game.
- Cross-world parties are supported. An invalid party size or role prevents startup; changes during a run cause a wait upon returning to the plaza.
- Stop before changing modes or counts. Selecting a mode applies its initial timing preset.

The title-bar X only hides the window: **active operations continue**. Enter `/makomaster` to reopen it. The window is movable and resizable, and fits its content when Advanced settings is expanded or collapsed.

## Teleporting and navigation

| Plugin | Feature |
| --- | --- |
| Lifestream | Teleport to the Gold Saucer from another area |
| vnavmesh | Navigate to the guide, registration NPC, and exchange NPC |

With the required dependencies enabled, Solo and Party leader modes travel to the Gold Saucer, speak to the guide to enter the plaza, and proceed to registration.

Solo and Party leader modes also work without these dependencies: **travel to the plaza manually and click Start within interaction range of the registration NPC**. Otherwise, the plugin waits and asks you to move there.

- **前往报名npc**: navigate to the side position beside registration without registering.
- **前往兑换npc**: navigate to the side position beside the exchange NPC without exchanging.

Navigation buttons are disabled when their dependencies are unavailable. At the bottom of Advanced settings, red means **Not installed**, yellow **Not enabled**, and green **Enabled**. Related operations can resume after dependencies are restored. In-game typing, Party member mode, and opening owned chests do not require these plugins. **TextAdvance and YesAlready are not required either.**

## Automatic chest exchange

While in the plaza, expand **Advanced settings (高级设置)** and enable **在自动排本时超过 … 点数后自动兑换宝箱**. Set the trigger threshold; the default is **9100**. This feature requires vnavmesh. Hover the setting for its availability hint.

Only during automatic play, holding **more than** the threshold in 魔光键影 Key Points triggers a visit to the exchange NPC and the **Other (其他)** category. After exchanging, the plugin returns to registration and continues the run.

**The threshold is a trigger, not a reserve.** Once triggered, as many available points as possible are exchanged for chests, until points or inventory space run out. Stop also disables automatic exchange.

## Advanced settings

Stop before editing key and word delays, or select a preset:

| Delay | Default (默认) | Fast (快速) | Diga (迪迦) |
| --- | --- | --- | --- |
| Each key | 40–100 ms | 20–50 ms | 10 ms |
| Each word | 800–1600 ms | 400–800 ms | 0 ms |

Changes apply when starting. Click **Save settings (保存设置)** to save the timing values. Diga still waits for IME responses and retains dangerous-skill pauses.

- **Chat output (聊天框输出)** is enabled by default. Uncheck it to silence plugin chat messages, including errors.
- **Save logs (保存日志)** is off by default and resets to off on reload. Check it to save logs and newly learned vocabulary. With saving off, new entries remain available only for the current plugin session. Enable it before reproducing an issue if you need diagnostic logs.

## While running

- Keep the game in the foreground. Switching away pauses operation; returning resumes it.
- Install and enable a Chinese input method. The plugin attempts to switch automatically; follow the window's instructions if its state cannot be confirmed.
- Turn off CapsLock and release Shift, Ctrl, Alt, and Win.
- Typing maintains focus on the minigame input field. Stop before using another input field.
- Dialogue advances without controlling the mouse. Typing pauses briefly around dangerous skills and resumes automatically.
- If navigation fails or an exchange result cannot be confirmed, read the window's message and stop before retrying.
