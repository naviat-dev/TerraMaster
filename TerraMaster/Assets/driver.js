const latitudeIndex = [[89, 12], [86, 4], [83, 2], [76, 1], [62, 0.5], [22, 0.25], [0, 0.125]];

console.log = (msg) => {
	window.chrome.webview.postMessage("LOG:" + msg);
};

console.error = (msg) => {
	window.chrome.webview.postMessage("ERR:" + msg);
};

async function getTileIndex(lat, lon) {
    const response = await fetch(`/api/tileindex/${lat}/${lon}`);
    const data = await response.json();
    return data.tileIndex;
}

window.onload = async () => {
	Cesium.Ion.defaultAccessToken = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJqdGkiOiI3MzQ5YzdiZS04OThlLTQ3YmMtODQ4ZS04MGJiNDZjYWNkNWQiLCJpZCI6MzEyNjkyLCJpYXQiOjE3NTAwODU1OTB9.vNCpYChTSQNmCN9s440YOJIMOOQXIWy4hB9m4LhAQlY';

	const imageryProvider = await Cesium.IonImageryProvider.fromAssetId(2); // Bing Maps Aerial

	const viewer = new Cesium.Viewer('cesiumContainer', {
		baseLayer: Cesium.ImageryLayer.fromProviderAsync(imageryProvider),
		terrain: Cesium.Terrain.fromWorldTerrain(),
	});

	const largeTilesDS = new Cesium.CustomDataSource("largeTiles");
	const smallTilesGrp = [];

	viewer.dataSources.add(largeTilesDS);
	for (var i = -90; i < 90; i += 10) {
		for (var j = -180; j < 180; j += 10) {
			largeTilesDS.entities.add({
				name: (i + 5) + "°, " + (j + 5) + "°",
				id: "lTile" + (i + 90) + "-" + (j + 180),
				rectangle: {
					coordinates: Cesium.Rectangle.fromDegrees(j, i, j + 10, i + 10),
					material: Cesium.Color.WHITE.withAlpha(0.01), // Invisible but clickable
					height: 0,
					heightReference: Cesium.HeightReference.RELATIVE_TO_GROUND,
					extrudedHeight: 10,
					outline: true,
					outlineColor: Cesium.Color.GRAY,
					outlineWidth: 4
				}
			});
		}
	}

	// Drawing all squares takes up too many resources and grinds rendering to a halt
	// Instead, squares are grouped by bounding box and added/removed from the render as the viewbox changes
	for (var i = -90; i < 90; i++) {
		var tileWidth = 0;
		var grpYInd = Math.floor(i / 10) + 9;
		for (var j = 0; j < latitudeIndex.length; j++) {
			if (Math.abs(i) >= latitudeIndex[j][0]) {
				tileWidth = latitudeIndex[j][1];
				break;
			}
		}
		if (smallTilesGrp.length <= grpYInd) smallTilesGrp.push([]);
		for (var j = -180; j < 180; j += tileWidth) {
			var grpXInd = Math.abs(i) >= 89 ? Math.floor(j / 12) + 15 : Math.floor(j / 10) + 18; // 12° at poles, 10° elsewhere
			if (smallTilesGrp[grpYInd].length <= grpXInd) smallTilesGrp[grpYInd].push(new Cesium.CustomDataSource("smallTiles" + grpYInd + "-" + grpXInd));
			smallTilesGrp[grpYInd][grpXInd].entities.add({
				name: (i + 0.5) + "°, " + (j + (tileWidth / 2)) + "°",
				id: "sTile" + (i + 90) + "-" + (j + 180),
				rectangle: {
					coordinates: Cesium.Rectangle.fromDegrees(j, i, j + tileWidth, i + 1),
					material: Cesium.Color.WHITE.withAlpha(0.01), // Invisible but clickable
					height: 0,
					heightReference: Cesium.HeightReference.RELATIVE_TO_GROUND,
					extrudedHeight: 10,
					outline: true,
					outlineColor: Cesium.Color.GRAY,
					outlineWidth: 4
				}
			});
		}
	}

	viewer.scene.globe.show = true;
	var viewbox, camPos, alt;
	var tileSize = 1; // 0 is small, 1 is large
	var boundsNPrev = 0, boundsSPrev = 0, boundsEPrev = 0, boundsWPrev = 0;

	// Runs every frame. Most drawing code should go here.
	viewer.scene.postRender.addEventListener(() => {
		viewbox = viewer.camera.computeViewRectangle();
		camPos = viewer.scene.camera.positionCartographic;
		alt = camPos.height;
		if (viewbox) {
			// These are the values that are actually used to determine if the viewbox has changed.
			// The viewbox can still move around, but should only trigger when tiles need to be added/removed.
			var boundsNCur = Math.floor(Cesium.Math.toDegrees(viewbox.north) / 10) + 9;
			var boundsSCur = Math.floor(Cesium.Math.toDegrees(viewbox.south) / 10) + 9;
			var boundsECur = Math.floor(Cesium.Math.toDegrees(viewbox.east) / 10) + 18;
			var boundsWCur = Math.floor(Cesium.Math.toDegrees(viewbox.west) / 10) + 18;
			// If the viewbox has changed, add/remove tiles as necessary
			if (boundsNCur !== boundsNPrev || boundsSCur !== boundsSPrev || boundsECur !== boundsEPrev || boundsWCur !== boundsWPrev) {
				boundsNPrev = boundsNCur;
				boundsSPrev = boundsSCur;
				boundsEPrev = boundsECur;
				boundsWPrev = boundsWCur;
				// This metric works in theory, but I should use the viewbox size instead of camera altitude
				if (alt < 2000000) {
					// Show smaller tiles and hide larger tiles
					if (tileSize) {
						tileSize = 0;
						viewer.dataSources.removeAll();
					}
					for (var i = boundsSCur; i <= boundsNCur; i++) {
						for (var j = boundsWCur; j <= boundsECur; j++) {
							// When the map mode switches from globe to 2D, the entire window hangs right here and crashes.
							if (i < smallTilesGrp.length && j < smallTilesGrp[i].length) {
								if (!viewer.dataSources.contains(smallTilesGrp[i][j])) {
									viewer.dataSources.add(smallTilesGrp[i][j]);
								}
							}
						}
					}
				} else {
					// Hide smaller tiles and show larger tiles
					if (!tileSize) {
						tileSize = 1;
						viewer.dataSources.removeAll();
						viewer.dataSources.add(largeTilesDS);
					}
				}
			}
		}
	});
};