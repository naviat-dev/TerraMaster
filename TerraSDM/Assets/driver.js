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

// Global variables to track state and prevent memory leaks
let viewer = null;
let largeTilesDS = null;
let smallTilesCache = new Map(); // Use Map for better memory management
let postRenderListener = null;
let frameCounter = 0;

// Cleanup function to properly dispose resources
function cleanup() {
	if (postRenderListener && viewer) {
		viewer.scene.postRender.removeEventListener(postRenderListener);
		postRenderListener = null;
	}
	
	if (viewer) {
		viewer.dataSources.removeAll();
		viewer.destroy();
		viewer = null;
	}
	
	if (smallTilesCache) {
		smallTilesCache.clear();
	}
	
	largeTilesDS = null;
}

// Create tiles on-demand instead of pre-creating all
function createLargeTileEntity(i, j) {
	return {
		name: (i + 5) + "°, " + (j + 5) + "°",
		id: "lTile" + (i + 90) + "-" + (j + 180),
		rectangle: {
			coordinates: Cesium.Rectangle.fromDegrees(j, i, j + 10, i + 10),
			material: Cesium.Color.WHITE.withAlpha(0.01),
			height: 0,
			heightReference: Cesium.HeightReference.RELATIVE_TO_GROUND,
			extrudedHeight: 10,
			outline: true,
			outlineColor: Cesium.Color.GRAY,
			outlineWidth: 4
		}
	};
}

function getOrCreateSmallTileDataSource(grpYInd, grpXInd) {
	const key = `${grpYInd}-${grpXInd}`;
	
	if (!smallTilesCache.has(key)) {
		smallTilesCache.set(key, new Cesium.CustomDataSource("smallTiles" + key));
	}
	
	return smallTilesCache.get(key);
}

function createSmallTileEntity(i, j, tileWidth) {
	return {
		name: (i + 0.5) + "°, " + (j + (tileWidth / 2)) + "°",
		id: "sTile" + (i + 90) + "-" + (j + 180),
		rectangle: {
			coordinates: Cesium.Rectangle.fromDegrees(j, i, j + tileWidth, i + 1),
			material: Cesium.Color.WHITE.withAlpha(0.01),
			heightReference: Cesium.HeightReference.RELATIVE_TO_GROUND,
			extrudedHeight: 10,
			outline: true,
			outlineColor: Cesium.Color.GRAY,
			outlineWidth: 4
		}
	};
}

window.onload = async () => {
	try {
		// Cleanup any existing instance
		cleanup();
		
		Cesium.Ion.defaultAccessToken = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJqdGkiOiI3MzQ5YzdiZS04OThlLTQ3YmMtODQ4ZS04MGJiNDZjYWNkNWQiLCJpZCI6MzEyNjkyLCJpYXQiOjE3NTAwODU1OTB9.vNCpYChTSQNmCN9s440YOJIMOOQXIWy4hB9m4LhAQlY';

		const imageryProvider = await Cesium.IonImageryProvider.fromAssetId(2);

		viewer = new Cesium.Viewer('cesiumContainer', {
			baseLayer: Cesium.ImageryLayer.fromProviderAsync(imageryProvider),
			terrain: Cesium.Terrain.fromWorldTerrain(),
		});

		largeTilesDS = new Cesium.CustomDataSource("largeTiles");
		
		// Only create large tiles initially - they're fewer in number
		for (let i = -90; i < 90; i += 10) {
			for (let j = -180; j < 180; j += 10) {
				largeTilesDS.entities.add(createLargeTileEntity(i, j));
			}
		}

		viewer.dataSources.add(largeTilesDS);
		viewer.scene.globe.show = true;

		let tileSize = 1; // 0 is small, 1 is large
		let boundsNPrev = 0, boundsSPrev = 0, boundsEPrev = 0, boundsWPrev = 0;
		let activeTileSources = new Set();

		// Throttle the postRender event to reduce CPU usage
		postRenderListener = () => {
			frameCounter++;
			// Only process every 10th frame (reduce from 60fps to 6fps for tile management)
			if (frameCounter % 10 !== 0) return;
			
			const viewbox = viewer.camera.computeViewRectangle();
			if (!viewbox) return;
			
			const alt = viewer.scene.camera.positionCartographic.height;
			
			// Calculate bounds with better precision
			const boundsNCur = Math.floor(Cesium.Math.toDegrees(viewbox.north) / 10) + 9;
			const boundsSCur = Math.floor(Cesium.Math.toDegrees(viewbox.south) / 10) + 9;
			const boundsECur = Math.floor(Cesium.Math.toDegrees(viewbox.east) / 10) + 18;
			const boundsWCur = Math.floor(Cesium.Math.toDegrees(viewbox.west) / 10) + 18;
			
			// Only update if bounds actually changed
			if (boundsNCur === boundsNPrev && boundsSCur === boundsSPrev && 
				boundsECur === boundsEPrev && boundsWCur === boundsWPrev) {
				return;
			}
			
			boundsNPrev = boundsNCur;
			boundsSPrev = boundsSCur;
			boundsEPrev = boundsECur;
			boundsWPrev = boundsWCur;

			if (alt < 2000000) {
				// Show smaller tiles - but create them on demand
				if (tileSize === 1) {
					tileSize = 0;
					// Remove large tiles but keep reference
					viewer.dataSources.remove(largeTilesDS);
					activeTileSources.clear();
				}
				
				// Add only visible small tile groups
				for (let i = boundsSCur; i <= boundsNCur && i >= 0 && i < 18; i++) {
					for (let j = boundsWCur; j <= boundsECur && j >= 0 && j < 36; j++) {
						const key = `${i}-${j}`;
						if (!activeTileSources.has(key)) {
							// Create small tiles on demand
							const dataSource = getOrCreateSmallTileDataSource(i, j);
							
							// Only populate if empty
							if (dataSource.entities.values.length === 0) {
								const latStart = (i - 9) * 10;
								for (let lat = latStart; lat < latStart + 10; lat++) {
									let tileWidth = 1; // Default width
									for (const [threshold, width] of latitudeIndex) {
										if (Math.abs(lat) >= threshold) {
											tileWidth = width;
											break;
										}
									}
									
									const lonStart = (j - 18) * 10;
									for (let lon = lonStart; lon < lonStart + 10; lon += tileWidth) {
										dataSource.entities.add(createSmallTileEntity(lat, lon, tileWidth));
									}
								}
							}
							
							if (!viewer.dataSources.contains(dataSource)) {
								viewer.dataSources.add(dataSource);
								activeTileSources.add(key);
							}
						}
					}
				}
				
				// Remove tiles outside view to free memory
				for (const key of activeTileSources) {
					const [i, j] = key.split('-').map(Number);
					if (i < boundsSCur || i > boundsNCur || j < boundsWCur || j > boundsECur) {
						const dataSource = smallTilesCache.get(key);
						if (dataSource && viewer.dataSources.contains(dataSource)) {
							viewer.dataSources.remove(dataSource);
							activeTileSources.delete(key);
						}
					}
				}
			} else {
				// Show large tiles
				if (tileSize === 0) {
					tileSize = 1;
					// Remove all small tiles
					activeTileSources.forEach(key => {
						const dataSource = smallTilesCache.get(key);
						if (dataSource && viewer.dataSources.contains(dataSource)) {
							viewer.dataSources.remove(dataSource);
						}
					});
					activeTileSources.clear();
					
					// Add large tiles back
					if (!viewer.dataSources.contains(largeTilesDS)) {
						viewer.dataSources.add(largeTilesDS);
					}
				}
			}
		};

		viewer.scene.postRender.addEventListener(postRenderListener);
		
	} catch (error) {
		console.error("Error initializing Cesium viewer:", error);
		cleanup();
	}
};

// Cleanup on page unload
window.addEventListener('beforeunload', cleanup);