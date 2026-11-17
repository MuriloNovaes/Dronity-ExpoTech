# ExpoTech AR

O drone do ExpoTech em realidade aumentada no navegador (AR.js + A-Frame). O modelo é servido
direto deste repositório pelo jsDelivr:

```
https://cdn.jsdelivr.net/gh/MuriloNovaes/Dronity-ExpoTech@ar-v1/ar/models/expo_drone.glb
```

## Usar

1. Abra no celular: **https://murilonovaes.github.io/Dronity-ExpoTech/ar/** e permita a câmera.
2. Aponte para o marcador do ExpoTech ([`marker/expotech-marker.png`](marker/expotech-marker.png)),
   impresso ou aberto na tela de outro aparelho. O drone aparece girando em cima dele.
3. Sem câmera ou no computador: [`preview.html`](preview.html) mostra o modelo em 3D.

Parâmetros da página:

| Parâmetro | Efeito |
|---|---|
| `?marker=hiro` | usa o marcador Hiro padrão do AR.js no lugar do marcador do ExpoTech |
| `?model=<url>` | carrega outro modelo GLB (ex.: um arquivo local para testar) |
| `?test=marker/test-scene.jpg` | usa uma imagem no lugar da câmera (teste sem celular) |

## Atualizar o modelo

1. Exporte o drone do Blender para `RobotDescription/expo_drone_source/Drone.glb`.
2. Rode `python ar/tools/prepare_glb.py` (tira o chão da cena, centraliza e apoia o drone no marcador).
3. Faça commit, crie uma tag nova (ex.: `ar-v2`) e troque `@ar-v1` por ela em `index.html` e `preview.html`.
   O jsDelivr guarda as versões por tag, por isso o modelo atualiza na hora com a tag nova.

O marcador é gerado por `python ar/tools/make_marker.py` (imagem + arquivo `expotech.patt`).
