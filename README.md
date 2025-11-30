# Shdowbus
Shadowverse 国际服 mod 工具

## 安装
首先安装 bepinex （mono 32位版本）
将本 dll 和 newtonsoft dll一同放到 bepinex 的 plugins 文件夹

## 功能
 - 修改原有卡牌
 - 新增自定义卡牌
 - 读取外部 png 文件作为卡图
 - 自定义我方和对方卡组（无视职业、token限制）
 - 热重载以上所有修改

## 卡牌修改/新增
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

## 卡图
将 png 放置在 `mods\cardimages\` 下即可，需要自行提前调整好宽高比

## 卡组
纯文本文件，每一行写一个卡牌 id 即可，#后面的文本会被无视，可以作注释用
