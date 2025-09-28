## About

Created by Marci599 for Mario Multiverse (created by neoarc)

This tool can be used to automatically generate spritesheet resources from individual recorded frames.

Download latest version here: [Releases](https://github.com/Marci599/sprite-rips-to-mm-sprite-resources/releases)

If you don't want to download the 25 MB .exe, use the python files from the source code.

## How to use

![Animation2](https://github.com/user-attachments/assets/898ad1a7-28e2-4b4d-a899-ae5f3af36b0a)

Structure your files as follows:
```
<Name>
├─ sprite_rips_to_mm_sprite_resources.exe
├─ <SubjectName>
│  └─ raw
│     ├─ <AnimationName>
│     │  ├─ frame001.png
│     │  └─ frame002.png
│     ├─ <AnimationName>
│     └─ <AnimationName>
├─ <SubjectName>
└─ <SubjectName>
```

You can download this example that contains a subject with raw frames: [V_Yoshi](https://github.com/Marci599/sprite-rips-to-mm-sprite-resources/blob/master/example.zip)
- Unzip it and put V_Yoshi next to the pragram.
- Note that the Jump aniamtions is just a placeholder.

After setup, launch the program, select a subject, configure options, and use 'Save & Generate' to create the spritesheet resources into `<SubjectName>/generated`.

For best results, if your subject moves around in the raw recording and you want to resize it, adjust each frame so it appears stationary before generating the spritesheet.
