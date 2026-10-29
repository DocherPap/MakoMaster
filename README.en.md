# MakoMaster

Automatic typing, repeat play, and chest opening for 魔光键影 on the mainland Chinese version of Final Fantasy XIV.

[简体中文](README.md)

## Installation

Add this URL under **Custom Plugin Repositories** in Dalamud settings. Save, refresh the plugin list, and install **MakoMaster**:

```text
https://raw.githubusercontent.com/DocherPap/MakoMaster/main/repo.json
```

Automatic teleporting requires **Lifestream**; automatic navigation requires **vnavmesh**.

## Usage

Enter `/makomaster`, select one of the four radio-button modes, then click **Start (开始)**. **Stop (停止)** ends operation and releases input-field focus maintenance.

| Mode | Preset when selected | Behavior |
| --- | --- | --- |
| Solo (单人排本) | Default | Select the NPC, register, accept departure, type, and exit results. Stop after the adjacent game count. |
| Party leader (组队队长) | Fast | Requires a four-person party and leadership. Register, accept departure, type, and exit results. Stop after the requested game count. |
| Party member (组队队员) | Fast | Requires a four-person party and non-leader status. Wait for the leader, accept departure, type, and exit results until stopped manually. |
| Open chests (开启宝箱) | Not applicable | Open the adjacent number of chests, stopping earlier if inventory runs out. |

- Game counts accept **0–9999**; **0** keeps running until stopped manually. A chest count of **0** opens until no matching chests remain in regular inventory bags.
- Normal and cross-world parties are supported. Party modes refuse to start with an invalid size or role. If these change during play, automation waits for recovery upon returning to the entry area.
- One results screen counts as one game; individual waves do not. Starting during a game resumes the current game.
- Solo and party-leader modes teleport to the Gold Saucer, speak to the guide to enter the plaza, and navigate to the registration NPC. Live NPC world coordinates take precedence. Stop before changing modes or counts.

## Plaza travel and chest exchange

- **前往报名npc** and **前往兑换npc** travel to the plaza and stop at the respective NPC’s side position without registering or exchanging.
- The automatic exchange checkbox is in **Advanced settings**, above key delays, with its threshold on the same line. It can only be enabled while in the plaza; hover the row for the hint. The default threshold is **9100**. During automatic queueing, holding strictly more Key Points triggers a visit to the exchange NPC and the Other (其他) category.
- The threshold triggers the exchange; it is not a reserve. The plugin exchanges as many points as possible for chests, subject to inventory space, then returns to registration and resumes an active run.
- Stop cancels owned travel/navigation and redemption, and disables automatic redemption. Missing dependencies, failed navigation or unverifiable purchases show a reason; stop before retrying.

## Advanced settings

The dependency list at the bottom shows red **Not installed**, yellow **Not enabled**, and green **Enabled**. Navigation buttons and automatic exchange are disabled when their dependencies are unavailable; hover for details. Solo and party-leader modes can still start: they register normally when the registration NPC is within interaction range, otherwise they wait for you to travel to the plaza manually. Disabling a dependency during a run pauses the related operation until restored. Navigation requires vnavmesh; travel from outside the Gold Saucer/plaza also requires Lifestream. Party-member mode, in-game typing, and opening owned chests remain available.

**Chat output (聊天框输出)** is enabled by default. Uncheck it to silence all plugin chat messages, including errors.

Expand **Advanced settings (高级设置)** to change the random delays. Stop automatic typing before editing them.

| Delay | Default | Fast preset | Diga preset (迪迦) |
| --- | --- | --- | --- |
| Each key | 40–100 ms | 20–50 ms | 10 ms |
| Each word | 800–1600 ms | 400–800 ms | 0 ms |

**预设：迪迦** applies the minimum allowed delays. IME response waits and dangerous-skill pauses still apply.

Click **Restore defaults (预设：默认)** or **Preset: Fast (预设：快速)** to apply a preset. Click **Save settings (保存设置)** to keep the values for future use.

**Save logs (保存日志)** is off by default and resets to off whenever the plugin reloads. Enable it to keep logs and newly learned vocabulary. Click **Disable logs (关闭日志)** to stop saving. While saving is disabled, newly learned entries remain available only for the current plugin session.

## While running

- Keep the game in the foreground. Switching away pauses operation; returning resumes it.
- Install and enable a Chinese input method. The plugin attempts to switch automatically; follow the window's instructions if switching fails or its mode cannot be verified.
- Turn off CapsLock and release modifier keys such as Shift, Ctrl, Alt, and Win.
- Automatic typing keeps restoring focus to the minigame's input field. Click **Stop (停止)** before using another input field.
- Typing pauses automatically around certain skills and resumes afterward.

Automatic dialogue uses internal UI controls without moving or clicking the mouse. Only prompts belonging to the active NPC interaction are confirmed.

TextAdvance and YesAlready are not runtime dependencies of this plugin.

The control window fits its content when opened and when Advanced settings is expanded or collapsed. It can also be moved and resized manually. The title-bar X hides it without stopping an active operation; enter `/makomaster` to reopen it.
