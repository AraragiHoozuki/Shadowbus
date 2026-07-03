# Shdowbus
Shadowverse 国际服 单机化 & mod 工具

## 功能
 - [x] 单机化，在游戏关服后可以继续游玩，目前支持查看主界面，编辑卡组（仅无限制），与CPU对战，开包（仅展示开包、翻牌动画，无实际效果）
 - [x] 默认全卡999张、全皮肤、全卡背，可以在卡组编辑时使用（不支持随机皮肤选项）
 - [x] 无限制卡组编辑: 无视职业及其他限制，可以往卡组放入任何卡牌（包含token），无数量限制，无卡组总张数限制。目前仅支持无限制的卡组。
 - [x] 自定义AI卡组
 - [ ] 强化AI
 - [ ] 卡牌Mod


## 安装
### 成品安装
到https://pan.baidu.com/s/1iNJ7HMVR2cbV1aKvLzI2AA?pwd=7ejh 下载 bepinex 和 本插件压缩包，解压到游戏根目录即可。

### 自行安装
1.首先安装 bepinex （mono 32位版本），从官网下载解压到根目录。
2.将本 shadowbus.dll 和 newtonsoft.dll一同放到 bepinex 的 plugins 文件夹。
3.从上面百度网盘的 shadowverse.zip 中解压出 Mods 文件夹放到游戏根目录


## 自定义 AI 卡组
在 `Mods/AISettings.json` 设置，将 `enable` 设置为 `true` 即启动自定义AI卡组。将 `deckName` 设置为自己的卡组名称即可生效。

其他选项目前无效

与AI对战的方式:游戏中选择 单人>对战 即可。


## 卡牌 Mod（开发中）
### 卡牌修改/新增
通过 `mods\cardmaster\` 中的 json 文件实现，json实例如下：
```json
[{
    "newCard": false,  // 为 true 表示新增卡牌，为 false 表示修改原有卡牌
    "cardId": 0, // 新增卡牌需要，任意卡牌id不能重复
    "templateCardId": 124541020, //模板卡牌，非新增时，表示修改的目标卡牌；新增时，新卡牌会默认继承模板卡牌的所有属性。
    "intFields": { //需要修改的所有整数属性，未写出的属性维持原数值
        "Cost": 5,
        "Atk": 5,
        "Life": 5,
        "ResourceCardId": 999900001 // 卡图相关，写成 mods\cardimages\ 下的 png 文件名（不含拓展名）表示将该 png 作为卡图
    },
    "stringChangeFields": {
       //需要修改的字符串属性（重写）
    },
    "stringAppendFields": {
        //需要修改的字符串属性（不会清除原有值，而是在其后面追加）
        "Skill": ",update_deck,banish",
        "SkillTiming": ",when_draw,when_draw",
        "SkillCondition": ",character=me&target=self,character=me&target=self",
        "SkillTarget": ",character=me&target=self,character=me&target=self",
        "SkillOption": ",type=add,none",
        "SkillPreprocess": ",none,none"
    },
    "localizationFields": {
        //文本属性，为空则表示不修改
        "CardName": "测试",
        "Description": "测试",
        "EvoDescription": "",
        "SkillDescription": "",
        "EvoSkillDescription": ""
    }
}]
```
### 卡图
将 png 放置在 `mods\cardimages\` 下即可，需要自行提前调整好宽高比

