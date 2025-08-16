var latitudeIndex = [[89, 12], [86, 4], [83, 2], [76, 1], [62, 0.5], [22, 0.25], [0, 0.125]];
const viewbox = viewer.camera.computeViewRectangle();
const alt = viewer.camera.positionCartographic.height;

console.log = (msg) => {
    window.chrome.webview.postMessage("LOG:" + msg);
};

console.error = (msg) => {
    window.chrome.webview.postMessage("ERR:" + msg);
};

window.onload = async () => {
    Cesium.Ion.defaultAccessToken = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJqdGkiOiI3MzQ5YzdiZS04OThlLTQ3YmMtODQ4ZS04MGJiNDZjYWNkNWQiLCJpZCI6MzEyNjkyLCJpYXQiOjE3NTAwODU1OTB9.vNCpYChTSQNmCN9s440YOJIMOOQXIWy4hB9m4LhAQlY';

    const imageryProvider = await Cesium.IonImageryProvider.fromAssetId(2); // Bing Maps Aerial

    const viewer = new Cesium.Viewer('cesiumContainer', {
        baseLayer: Cesium.ImageryLayer.fromProviderAsync(imageryProvider),
        terrain: Cesium.Terrain.fromWorldTerrain(),
    });

    viewer.scene.globe.show = true;

    /* This will draw every terrain tile. It should be noted that drawing every tile takes up bucketloads of resources, and should not be run in its current state.
    Once paired with viewboxes, this will be more useful.
    for (var i = -90; i < 90; i++) {
        var tileWidth = 0;
        for (var j = 0; j < latitudeIndex.length; j++) {
            if (Math.abs(i) >= latitudeIndex[j][0]) {
                tileWidth = latitudeIndex[j][1];
                break;
            }
        }
        for (var j = -180; j < 180; j += tileWidth) {
            viewer.entities.add({
                rectangle: {
                    coordinates: Cesium.Rectangle.fromDegrees(j, i, j + tileWidth, i + 1),
                    material: Cesium.Color.BLUE.withAlpha(0.4),
                    height: 0, // <-- this disables terrain clamping
                    outline: true,
                    outlineColor: Cesium.Color.GRAY,
                    outlineWidth: 2
                }
            });
        }
    } */
};