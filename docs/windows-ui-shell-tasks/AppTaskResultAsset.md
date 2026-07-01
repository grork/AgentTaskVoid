# AppTaskResultAsset

`Windows.UI.Shell.Tasks.AppTaskResultAsset` — represents one asset produced by a completed task (a file or other generated content). `sealed class`, `Experimental`, contract v1.0. Used only as an element of the array passed to `AppTaskContent.CreateGeneratedAssetsResult` — see [AppTaskContent.md](AppTaskContent.md).

## Constructor

```csharp
AppTaskResultAsset(string name, string context, Uri iconUri, Uri assetUri)
```

| Param | Notes |
|---|---|
| `name` | Display name shown to the user, e.g. `"ResultStudy.txt"`. |
| `context` | Extra context shown next to the name, e.g. `"Generated content"`. |
| `iconUri` | Icon representing the asset. Supports `ms-appx:///`, `ms-appdata:///`, absolute paths — see [README](README.md#uri-formats-accepted-by-iconasset-parameters). |
| `assetUri` | URI of the actual generated asset (e.g. a file path). |

No other members — no properties, no additional methods. It's a plain, immutable value carrier constructed and then handed straight to `AppTaskContent.CreateGeneratedAssetsResult`.
