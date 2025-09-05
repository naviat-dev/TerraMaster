const latitudeIndex = [[89, 12], [86, 4], [83, 2], [76, 1], [62, 0.5], [22, 0.25], [0, 0.125]];

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

    const largeTilesDS = new Cesium.CustomDataSource("largeTiles");
    const smallTilesDS = new Cesium.CustomDataSource("smallTiles");

    // viewer.dataSources.add(smallTilesDS);
    viewer.dataSources.add(largeTilesDS);
    for (var i = -90; i < 90; i += 10) {
        for (var j = -180; j < 180; j += 10) {
            largeTilesDS.entities.add({
                name: (i + 5) + "°, " + (j + 5) + "°",
                rectangle: {
                    coordinates: Cesium.Rectangle.fromDegrees(j, i, j + 10, i + 10),
                    material: Cesium.Color.WHITE.withAlpha(0.01), // Invisible but clickable
                    height: 0,
                    heightReference: Cesium.HeightReference.RELATIVE_TO_GROUND,
                    extrudedHeight: 10,
                    outline: true,
                    outlineColor: Cesium.Color.GRAY,
                    outlineWidth: 2
                }
            });
        }
    }

    // Drawing all squares takes up too many resources and grinds rendering to a halt
    // Instead, squares are grouped by bounding box and added/removed from the render as the viewbox changes
    for (var i = -90; i < 90; i++) {
        var tileWidth = 0;
        for (var j = 0; j < latitudeIndex.length; j++) {
            if (Math.abs(i) >= latitudeIndex[j][0]) {
                tileWidth = latitudeIndex[j][1];
                break;
            }
        }
        for (var j = -180; j < 180; j += tileWidth) {
                smallTilesDS.entities.add({
                    name: (i + 0.5) + "°, " + (j + (tileWidth / 2)) + "°",
                    rectangle: {
                        coordinates: Cesium.Rectangle.fromDegrees(j, i, j + tileWidth, i + 1),
                        material: Cesium.Color.WHITE.withAlpha(0.01), // Invisible but clickable
                        height: 0,
                        heightReference: Cesium.HeightReference.RELATIVE_TO_GROUND,
                        extrudedHeight: 10,
                        outline: true,
                        outlineColor: Cesium.Color.GRAY,
                        outlineWidth: 2
                    }
                });
        }
    }

    viewer.scene.globe.show = true;
    var viewbox;
    var camPos;
    var alt;
    var tileSize = 1; // 0 is small, 1 is large

    // Runs every frame. Most drawing code should go here.
    viewer.scene.postRender.addEventListener(() => {
        viewbox = viewer.camera.computeViewRectangle();
        camPos = viewer.scene.camera.positionCartographic;
        alt = camPos.height;
        if (viewbox) {
            if (alt < 2000000) {
                // Show smaller tiles and hide larger tiles
                if (tileSize) {
                    tileSize = 0;
                    // smallTilesDS.show = true;
                    largeTilesDS.show = false;
                }
            } else {
                // Hide smaller tiles and show larger tiles
                if (!tileSize) {
                    tileSize = 1;
                    // smallTilesDS.show = false;
                    largeTilesDS.show = true;
                }
            }
        }

        console.log("Camera Position:" + camPos);
        console.log("Altitude:" + alt);
        // console.log("Viewbox:" + viewbox.west + "," + viewbox.south + "," + viewbox.east + "," + viewbox.north);
    });



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