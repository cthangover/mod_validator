# Validator — 不玩游戏也能检查模组

Validator 会读取运行中游戏的每一个模组——清单、数据文件、剧本、场景补丁和 C# 源码——并报告损坏或可疑之处：指向无人声明的物品、场景或背景的引用、未知的剧本命令、错误的参数、缺失的依赖。每次启动游戏时它都会在后台运行并写出报告，错字会在玩家走到那一行之前就被发现。

它检查的是文件里能看到的东西。它不玩游戏：Validator 放行的剧本仍可能与作者的本意不同，而运行时才拼出 id 的代码无从追查。

## 一个错误及其修正

给玩家加狼肉的剧本行，物品 id 打了错字：

```scenario
scene: home_kitchen
priority: 0
---
action item.add id=food/wolf_meet count=1
end
```

下次启动 Cthangover 后，报告里会有一条：

```json
{
  "severity": "error",
  "code": "REF_NOT_FOUND",
  "message": "item 'food/wolf_meet' is referenced but never declared",
  "mod": "cthangover_game",
  "file": "scenarios/doc_validator_demo.scenario",
  "line": 4,
  "suggestion": "declare item 'food/wolf_meet' or fix the reference",
  "related": null
}
```

模组、文件和行号都指向这个错误。改回 `food/wolf_meat` 问题就消失了，汇总里的错误计数少了一（这次运行从 18 降到 17）。

## 如何运行、如何读报告

- 进入包含它的游戏时（Cthangover 把它和开发者工具一起附带）它会自动开始，在模组加载之后，于后台线程运行；不会阻塞游戏。
- 每次运行都会往日志写一行：`validator: started, 33 mod(s) queued`，然后是错误、警告和信息三类总数。
- 报告写在 `user://validation/report.json`（游戏用户数据文件夹里）。里面有汇总、被检查的模组及其版本与校验和，以及问题清单。

每个问题都有：

- `severity` —— `error`（有东西坏了）、`warning`（多半是个错误）或 `info`（值得看一眼，比如没有任何东西引用的物品）；
- `code` —— 问题种类，例如 `REF_NOT_FOUND`、`JSON_PARSE_ERROR`、`SCHEMA_MISSING_FIELD`、`SCHEMA_UNKNOWN_PARAM`、`DECL_UNUSED`、`DYNAMIC_KEY`；
- `mod`、`file`、`line` —— 去哪里修；`suggestion` —— 怎么修。

`DYNAMIC_KEY` 类的信息表示键是运行时拼出来的，Validator 无法检查；它们不是错误。

## 检查什么

检查分三个阶段：

1. **模式** —— `manifest.json` 有字符串 `id` 和 `version`；JSON 文件能解析。
2. **契约** —— 每个模组声明什么、引用什么：各种类型的实体（角色、物品、任务、技能、配方、动作，以及模式描述的类型）、剧本命令和动作及其参数、场景补丁、C# 代码读写过的属性。
3. **对账** —— 每条引用都有声明，`depends` 覆盖模组从别人那里用到的东西，没有被动用的声明会列出。

剧本动作和参数会对照模组附带的各种模式来检查（见 Schemas），规则与命令面板相同。

## 设置

在模组管理器中该模组的设置标签页里：

- **Enabled**、**Run on start** —— 关掉检查；
- **Parse C#**、**Parse GDScript**（默认关闭，速度慢）、**Parse scenarios** —— 读取哪些来源；
- **Write report**、**Report path** —— `report.json` 写到哪里；
- **Block mods on error** —— 设置里有这一项，但在当前版本它不起任何作用：模组永远不会被拦截。

## 出问题了怎么办

1. **没有报告。** 检查还没结束游戏就关了（对 Cthangover 大约要半分钟），或者 **Write report** 关着。
2. **很多 `DYNAMIC_KEY` 条目。** 对会拼键的代码来说很正常；它们只是信息。
