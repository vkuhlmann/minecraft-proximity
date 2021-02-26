# Minecraft Proximity
**Bringing proximity chat to Minecraft!**

This application reads your Minecraft coordinates from the screen (use HUD from Vanilla Tweaks or provisionally the F3 screen).
Then, it uses the Discord Game API to set the volumes of you and your party. And then: have fun!

My one-take demo (not great, but for now sufficient I'd say): https://youtu.be/kvT7hmtxVss

_The project is still early in its development. Please contact me if you want to help test it._

***

NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG.

This application is still very much in development. I would appreciate any engagement. If you are on the TitanCraft server, you
can find me there. Alternatively, send me a mail using the e-mail address which you can find on my GitHub profile page.

Make sure to read LICENSE.txt and LICENSES.txt before using and modifying.

## How to build

Publishing the application is done by the `dotnet publish` command from the .NET Core SDK. For your own machine, you can also open the
project in Visual Studio and compile from there. Refer to [dotnet publish command - Microsoft Docs](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-publish) for the documentation.

1. Download the code as zip or clone it to your computer.
2. Navigate in your terminal to the `src/` directory of the repository.
3. For Windows 64-bit, execute
```
dotnet publish -r win-x64 -c Release -p:PublishTrimmed=true
```

4. Open the `publish` directory mentioned by the command output.
5. The directory is flooded with DLL's. You could use it like this...
6. ... or, what I did: I used the trick of [dnSpy's AppHostPatcher](https://github.com/dnSpy/dnSpy/blob/master/Build/AppHostPatcher/Program.cs). Compile it. Navigate your terminal to the `publish` directory, and execute
```
"D:\path\to\apphostpatcher\apphostpatcher.exe" MinecraftProximity.exe -d bin
```
Then, move all files is `publish/` to a new subdirectory `publish/bin/`. Only the `.exe` should remain in the `publish/` directory.

7. Over at the [Discord SDK Start Guide](https://discord.com/developers/docs/game-sdk/sdk-starter-guide), download the Discord Game SDK zip. If your compilation is 64-bit, copy the `lib/x86_64/discord_game_sdk.dll` file to your `publish/bin/` directory (or `publish/` if you have skipped the trick above). For 32-bit, this is `lib/x84/discord_game_sdk.dll`.
8. Recommended (though optional) cleaning up: copy the top-level files of the reposity (everything except `src/`) to the `publish/` directory. Now copy your publish directory to wherever you like the MinecraftProximity program to reside. 
9. Run MinecraftProximity.exe. It should... work? If something goes wrong, make sure to contact me.

