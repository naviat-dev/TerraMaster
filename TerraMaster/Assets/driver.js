
const viewer = new Cesium.Viewer('cesiumContainer', {
    terrainProvider: Cesium.createWorldTerrainAsync()
});

window.addMarker = function (lat, lon, name) {
    viewer.entities.add({
        name: name,
        position: Cesium.Cartesian3.fromDegrees(lon, lat),
        point: { pixelSize: 10, color: Cesium.Color.YELLOW },
        label: { text: name, pixelOffset: new Cesium.Cartesian2(0, -20) }
    });
};

viewer.screenSpaceEventHandler.setInputAction(function (movement) {
    const picked = viewer.scene.pick(movement.position);
    if (picked && picked.id) {
        window.external.notify("clicked:" + picked.id.name);
    }
}, Cesium.ScreenSpaceEventType.LEFT_CLICK);