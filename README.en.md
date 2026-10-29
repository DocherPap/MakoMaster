# MakoMaster

Automatic typing, repeat play, and chest opening for 魔光键影 on the mainland Chinese version of Final Fantasy XIV.

[简体中文](README.md)

## Installation

Add this URL under **Custom Plugin Repositories** in Dalamud settings. Save, refresh the plugin list, and install **MakoMaster**:

```text
https://raw.githubusercontent.com/DocherPap/MakoMaster/main/repo.json
```

## Start and stop

1. Enter `/makomaster` to open the control window.
2. Stand near the entry NPC, or enter an ongoing minigame, then click **Start (开始)**.
3. **Start (开始)** keeps running: register, type, exit results, and start the next game until you click Stop. Starting during a game resumes the current game.
4. Click **Stop (停止)** to end operation and release input-field focus maintenance. F8 and Esc do not stop the plugin.

## Repeat play

Check **Repeat play (循环开局)** to enable four-person party handling, including cross-world parties. The leader registers with the entry NPC; other members wait for the leader and accept departure. Everyone types and exits results automatically, then continues to the next game until **Stop (停止)** is clicked. Outside a four-person party, party handling turns off and normal automatic play continues.

One results screen counts as one game; individual waves do not. Move closer if the NPC is out of reach. The plugin does not move your character.

## Open chests

Stop automatic typing or repeat play first. In the **魔光键影宝箱** section, enter a quantity:

- **Greater than 0:** open that many chests, or stop when your supply runs out.
- **0:** continue until no matching chests remain in your regular inventory bags.

Click **Start opening (开始开箱)**. Click **Stop opening (停止开箱)** or the main **Stop (停止)** button to end the operation.

## Advanced settings

Expand **Advanced settings (高级设置)** to change the random delays. Stop automatic typing before editing them.

| Delay | Default | Fast preset | Diga preset (迪迦) |
| --- | --- | --- | --- |
| Each key | 40–100 ms | 20–50 ms | 10 ms |
| Each word | 800–1600 ms | 400–800 ms | 0 ms |

**预设：迪迦** applies the minimum allowed delays. IME response waits and dangerous-skill pauses still apply.

Click **Restore defaults (恢复默认间隔)** or **Preset: Fast (预设：快速)** to apply a preset. Click **Save settings (保存设置)** to keep the values for future use.

**Save logs (保存日志)** is off by default and resets to off whenever the plugin reloads. Enable it to keep logs and newly learned vocabulary. Click **Disable logs (关闭日志)** to stop saving. While saving is disabled, newly learned entries remain available only for the current plugin session.

## While running

- Keep the game in the foreground. Switching away pauses operation; returning resumes it.
- Install and enable a Chinese input method. The plugin attempts to switch automatically; follow the window's instructions if switching fails or its mode cannot be verified.
- Turn off CapsLock and release modifier keys such as Shift, Ctrl, Alt, and Win.
- Automatic typing keeps restoring focus to the minigame's input field. Click **Stop (停止)** before using another input field.
- Typing pauses automatically around certain skills and resumes afterward.
