# 第三方元件

## Virtual Display Driver

VibeDeck 的「建立虛擬螢幕」會在使用者明確操作後，下載並安裝下列第三方元件：

- Virtual Display Driver 25.7.23 — MIT License
  - https://github.com/VirtualDrivers/Virtual-Display-Driver
  - SHA-256: `e24210692b442b39af763536330ce78b423f19342b7a7792c26de3944e418b3a`
- NefCon 1.14.0 — MIT License
  - https://github.com/nefarius/nefcon
  - SHA-256: `a15557da24a9efca203158de3b43b0eaf982db231f0194031f1ed428bc13e669`

安裝器固定版本、驗證下載檔雜湊與驅動 Authenticode 簽章，驗證失敗時不會安裝。VibeDeck 不會關閉 Secure Boot，也不會開啟 Windows 測試簽章模式。



## Managed HTTPS Connector

VibeDeck Windows Setup bundles:

- cloudflared 2026.7.2
  - Source: https://github.com/cloudflare/cloudflared
  - The packaging script pins the binary and license SHA-256 values and verifies the binary's Cloudflare, Inc. Authenticode signature.
  - The upstream license text is bundled in each installation at `licenses/cloudflared-LICENSE.txt`.
