# Raid event package fixtures

Unchanged raw tables from the public [Events Gallery](https://github.com/projectpokemon/EventsGallery)
at commit `154d81be88453f6f78ec1d6d86e85fe0f2f5c240`. Every file's git blob hash matches that tree.
They are used only to test package identification and staged import; they make no claim about
in-game behavior. Variants (older layouts, renames, truncation) are derived in temporary folders by
`RaidEventImportTests`.

| Folder | Source path | Layout |
| --- | --- | --- |
| `swsh-index-117` | `Released/Gen 8/SwSh/Wild Area Events/Index 117` | Sword/Shield, 6 tables (Crown Tundra) |
| `sv-148-meowscarada-r2` | `Released/Gen 9/SV/Raid Events/148 Meowscarada the Unrivaled Round 2/Files` | Scarlet/Violet, `_3_0_0` tables |

SHA-256:

```
244adda9d2a0adda1cba3bf45e35777f51b221a9f00c9fe136e54e8a91f0a40b  swsh-index-117/bonus_rewards
1aa96293127ecfb576f21255bb460c8b3da83dbbc827877ad8657a410d869831  swsh-index-117/dai_encount
fe054f8a5f01220957a9a7e2078b938df89bdba4a93a717d8d93d93e91f1eebc  swsh-index-117/drop_rewards
e5a35406159d410f8a73b29f80633aa54748ed514d268d61b51b205a767e9d95  swsh-index-117/normal_encount
b4ab8dbea68d944d8020fbd002a982bdf737c4e86435d26f6bc2f38dd41b08be  swsh-index-117/normal_encount_rigel1
2ccccfcd0f2c1f805a9ef5b3b9824e4c8067b5efd3fb17d70c601a60bb081e65  swsh-index-117/normal_encount_rigel2
98dd43a7e515f2f72560353dd22db8f76d9624750da4409b46c47fb42153568b  sv-148-meowscarada-r2/event_raid_identifier_3_0_0
94bfbda0b4657d1ecefabb9e81c289009f1a6083f5d71067ad7a446e241b12e9  sv-148-meowscarada-r2/fixed_reward_item_array_3_0_0
dd56593cf4a6000be9e1d9cfa2f57ce64e13d3cb9b6c21d049c50464e84c1c13  sv-148-meowscarada-r2/lottery_reward_item_array_3_0_0
450956382295038a48d9cc128c11ecc1a0ed2f476380eeb2cedf367a6beb50e0  sv-148-meowscarada-r2/raid_enemy_array_3_0_0
f9f1437a82be809e2697fa6615d144ffcb5ddad4a3d8454734ba29c84509c57c  sv-148-meowscarada-r2/raid_priority_array_3_0_0
```
