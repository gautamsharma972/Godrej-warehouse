(function () {
    var host = document.getElementById("canvas-host");
    var warningEl = document.getElementById("warning");
    var legendEl = document.getElementById("legend");
    var hintEl = document.getElementById("hint");
    var vehicleInfoLineEl = document.getElementById("vehicle-info-line");
    var edgeLengthLineEl = document.getElementById("edge-length-line");
    var skuRailEl = document.getElementById("sku-rail");
    var dragGhostEl = document.getElementById("drag-ghost");
    var optionsListEl = document.getElementById("options-list");
    var newOptionBtnEl = document.getElementById("new-option-btn");
    var groupsListEl = document.getElementById("groups-list");
    var searchInputEl = document.getElementById("search-input");
    var searchResultsEl = document.getElementById("search-results");
    var layerChipsEl = document.getElementById("layer-chips");

    document.querySelectorAll(".rail-section.collapsible .rail-title").forEach(function (title) {
        title.addEventListener("pointerup", function (ev) {
            ev.stopPropagation();
            var section = title.closest(".rail-section");
            var shouldCollapse = !section.classList.contains("collapsed");
            section.classList.toggle("collapsed", shouldCollapse);
            title.setAttribute("aria-expanded", shouldCollapse ? "false" : "true");
        });
    });

    function showError(message) {
        warningEl.textContent = "3D preview error: " + message;
        warningEl.style.display = "block";
    }

    var scene = new THREE.Scene();
    scene.background = new THREE.Color(0xf5fbf9);

    var camera = new THREE.PerspectiveCamera(45, 1, 1, 20000);
    var renderer = new THREE.WebGLRenderer({ antialias: true });
    host.appendChild(renderer.domElement);
    var canvas = renderer.domElement;

    scene.add(new THREE.AmbientLight(0xffffff, 0.82));
    var sun = new THREE.DirectionalLight(0xffffff, 0.72);
    sun.position.set(400, 800, 300);
    scene.add(sun);

    var sceneGroup = new THREE.Group();
    scene.add(sceneGroup);

    // ---- camera: spherical orbit around a target. All input writes the des* values;
    // the animate loop eases the actual camera toward them every frame, so orbit, pan,
    // zoom, view-mode switches and focus jumps all glide instead of snapping. ----
    var target = new THREE.Vector3(0, 0, 0);
    var azimuth = 0.5, polar = 1.1, radius = 500;
    var desAzimuth = azimuth, desPolar = polar, desRadius = radius;
    var desTarget = target.clone();
    var vehicleCenter = target.clone();
    var minRadius = 50, maxRadius = 4000;
    var lastVehicleKey = null;
    var floorGridMat = new THREE.LineBasicMaterial({ color: 0xcbdad6, transparent: true, opacity: 0.55 });

    function easeCamera() {
        var k = 0.22;
        azimuth += (desAzimuth - azimuth) * k;
        polar += (desPolar - polar) * k;
        radius += (desRadius - radius) * k;
        target.lerp(desTarget, k);
        // The closer the camera gets to the cargo, the more the ground grid fades out -
        // zooming reads as "into the truck", not "magnifying the warehouse floor".
        if (maxRadius > minRadius) {
            var t = (radius - minRadius) / (maxRadius - minRadius);
            floorGridMat.opacity = 0.08 + 0.38 * Math.max(0, Math.min(1, t));
        }
        applyCamera();
    }

    // Steer toward the equivalent angle closest to where the camera already is, so a
    // view-mode switch after lots of orbiting doesn't unwind full revolutions.
    function toNearestAngle(current, targetAngle) {
        var diff = (targetAngle - current) % (2 * Math.PI);
        if (diff > Math.PI) diff -= 2 * Math.PI;
        if (diff < -Math.PI) diff += 2 * Math.PI;
        return current + diff;
    }

    // "3d" allows free orbit/idle-rotate; "top"/"side"/"sideRight"/"layer" are fixed camera
    // presets where a one-finger drag PANS instead of orbiting (pinch-zoom works
    // everywhere) - simpler than swapping in a true THREE.OrthographicCamera,
    // which isn't needed for a truck-sized scene. "layer" is top-down plus a
    // height-band visibility filter driven by the chips along the bottom.
    var currentViewMode = "3d";
    var layerFilterY = 0;
    var layerBandCm = 30;

    function applyCamera() {
        var vertical = radius * Math.cos(polar);
        var horizontal = radius * Math.sin(polar);
        camera.position.set(
            target.x + horizontal * Math.sin(azimuth),
            target.y + vertical,
            target.z + horizontal * Math.cos(azimuth)
        );
        camera.lookAt(target);
    }

    // Only re-frames the camera the first time a given vehicle size is seen, so
    // a user's manual orbit/zoom survives every subsequent live re-render (qty
    // edits, reorders) instead of snapping back on every keystroke.
    //
    // Frames the whole vehicle by fitting its bounding sphere inside the camera's
    // frustum (both vertical and horizontal FOV) rather than a single-axis
    // heuristic - a fixed "maxDim * constant" radius looks fine for near-cubic
    // vehicles but puts the camera almost end-on for a long, narrow truck
    // (e.g. 960x240x260cm), which is what was happening before this fix.
    function initCameraForVehicle(vehicle) {
        var halfDiag = 0.5 * Math.sqrt(
            vehicle.widthCm * vehicle.widthCm +
            vehicle.heightCm * vehicle.heightCm +
            vehicle.lengthCm * vehicle.lengthCm);

        target.set(0, vehicle.heightCm * 0.5, vehicle.lengthCm * 0.5);
        desTarget.copy(target);
        vehicleCenter.copy(target);

        var aspect = camera.aspect || 1;
        var vFov = camera.fov * Math.PI / 180;
        var hFov = 2 * Math.atan(Math.tan(vFov / 2) * aspect);
        var distV = halfDiag / Math.sin(vFov / 2);
        var distH = halfDiag / Math.sin(hFov / 2);

        azimuth = desAzimuth = 0.6;
        polar = desPolar = 1.15;
        radius = desRadius = Math.max(distV, distH) * 0.78;
        // Zoom range hugs the truck: close enough to inspect a single carton, far enough
        // to frame the whole vehicle - never the empty warehouse around it.
        minRadius = halfDiag * 0.24;
        maxRadius = halfDiag * 1.65;
        applyCamera();
    }

    function applyViewMode(mode) {
        var previous = currentViewMode;
        currentViewMode = mode || "3d";
        if (currentViewMode === "top" || currentViewMode === "layer") {
            desAzimuth = toNearestAngle(desAzimuth, 0);
            desPolar = 0.05; // looking almost straight down
        } else if (currentViewMode === "side") {
            // Mostly level, tilted just enough that floor-plane raycasts (tap/drop
            // placement) still land somewhere sensible instead of running parallel.
            desAzimuth = toNearestAngle(desAzimuth, Math.PI / 2);
            desPolar = 1.32;
        } else if (currentViewMode === "sideRight") {
            // Opposite side elevation - same tilt, mirrored around the vehicle.
            desAzimuth = toNearestAngle(desAzimuth, -Math.PI / 2);
            desPolar = 1.32;
        }
        layerChipsEl.style.display = currentViewMode === "layer" ? "flex" : "none";
        if (previous !== currentViewMode) {
            applyLayerVisibility();
        }
    }

    function applyLayerVisibility() {
        var filtering = currentViewMode === "layer";
        for (var i = 0; i < boxMeshes.length; i++) {
            var m = boxMeshes[i];
            m.visible = !filtering ||
                (m.userData.yCm < layerFilterY + layerBandCm && m.userData.yCm + m.userData.hCm > layerFilterY);
        }
    }

    var lastLayerChipsKey = null;
    function rebuildLayerChips(vehicle) {
        var key = vehicle.heightCm + ":" + layerFilterY;
        if (key === lastLayerChipsKey) return;
        lastLayerChipsKey = key;

        layerChipsEl.innerHTML = "";
        for (var y = 0; y < vehicle.heightCm; y += layerBandCm) {
            (function (bandY) {
                var chip = document.createElement("div");
                chip.className = "layer-chip" + (Math.abs(bandY - layerFilterY) < 0.01 ? " active" : "");
                chip.textContent = Math.round(bandY * 10) + " mm";
                chip.addEventListener("pointerup", function (ev) {
                    ev.stopPropagation();
                    layerFilterY = bandY;
                    rebuildLayerChips(vehicle);
                    applyLayerVisibility();
                });
                layerChipsEl.appendChild(chip);
            })(y);
        }
    }

    function resize() {
        var w = host.clientWidth || 1;
        var h = host.clientHeight || 1;
        camera.aspect = w / h;
        camera.updateProjectionMatrix();
        renderer.setSize(w, h, false);
    }
    if (window.ResizeObserver) {
        new ResizeObserver(resize).observe(host);
    } else {
        window.addEventListener("resize", resize);
    }
    resize();

    // ---- placement state: "active" just means the host's quantity-entry popup is open
    // for a new group - the zone it lands in is chosen by which of the 6 fixed floor
    // zones a SKU chip is dragged onto (or defaults to Front-Left for a plain tap), never
    // by a continuous floor position. Orbit/pinch/tap-select are suppressed while active
    // so they don't fight the SKU-chip drag gesture. ----
    var placementActive = false;
    var placementPlaneY = 0;
    var lastPreviewDims = null; // {w,h,d} in vehicle-local cm, from the last server-computed preview
    var currentVehicle = { widthCm: 0, lengthCm: 0, heightCm: 0 };
    var lastPlacedBoxes = []; // per-carton vehicle-local AABBs from the last render, for live collision checks

    var ghostMesh = null;
    var ghostValid = true;

    var raycastPlane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
    var planeRaycaster = new THREE.Raycaster();

    // Fixed 6-zone floor split: thirds of length (Front/Middle/Back) x halves of width
    // (Left/Right) - must match OutwardLoadPlanService.ZoneFootprint exactly so the visual
    // zone a supervisor drags onto is the same zone the server places into.
    var zoneLengthNames = ["Front", "Middle", "Back"];
    var zoneWidthNames = ["Left", "Right"];

    function zoneForVehicleXZ(x, z) {
        var zoneLengthCm = currentVehicle.lengthCm / 3;
        var zoneWidthCm = currentVehicle.widthCm / 2;
        var lengthIdx = zoneLengthCm > 0 ? Math.min(2, Math.max(0, Math.floor(z / zoneLengthCm))) : 0;
        var widthIdx = zoneWidthCm > 0 ? Math.min(1, Math.max(0, Math.floor(x / zoneWidthCm))) : 0;
        return { zoneLength: zoneLengthNames[lengthIdx], zoneWidth: zoneWidthNames[widthIdx] };
    }

    // Delivery Unloading View "Show" action: snap the camera to frame just this group and give it
    // a brief highlight pulse. An instant reframe rather than an animated tween, kept simple.
    function focusOnBox(groupId) {
        var boxesForGroup = lastPlacedBoxes.filter(function (b) { return b.groupId === groupId; });
        if (boxesForGroup.length === 0) return;

        var minX = Infinity, minY = Infinity, minZ = Infinity, maxX = -Infinity, maxY = -Infinity, maxZ = -Infinity;
        for (var i = 0; i < boxesForGroup.length; i++) {
            var b = boxesForGroup[i];
            minX = Math.min(minX, b.x); minY = Math.min(minY, b.y); minZ = Math.min(minZ, b.z);
            maxX = Math.max(maxX, b.x + b.w); maxY = Math.max(maxY, b.y + b.h); maxZ = Math.max(maxZ, b.z + b.d);
        }

        var centerX = (minX + maxX) / 2 - currentVehicle.widthCm / 2;
        var centerY = (minY + maxY) / 2;
        var centerZ = (minZ + maxZ) / 2;
        desTarget.set(centerX, centerY, centerZ);

        var halfDiag = 0.5 * Math.hypot(maxX - minX, maxY - minY, maxZ - minZ);
        var vFov = camera.fov * Math.PI / 180;
        desRadius = Math.max(halfDiag / Math.sin(vFov / 2) * 1.8, 80);

        selectedKey = "g" + groupId;
        focusedGroupId = groupId;
        refreshSelectionVisuals();
        markSearchSelection();
    }

    // ---- pointer interaction: one-finger drag orbits (3D view only, suppressed while
    // the quantity popup is open); two-finger pinch zooms (any view); a plain tap
    // selects a box/group ----
    var pointers = {};
    var dragMoved = false;
    var lastInteraction = 0;
    var selectedKey = null;
    var focusedGroupId = null;
    var boxMeshes = [];

    function pointerIds() { return Object.keys(pointers); }

    canvas.addEventListener("pointerdown", function (ev) {
        canvas.setPointerCapture(ev.pointerId);
        pointers[ev.pointerId] = { x: ev.clientX, y: ev.clientY };
        dragMoved = false;
        lastInteraction = performance.now();
    });

    canvas.addEventListener("pointermove", function (ev) {
        if (!pointers[ev.pointerId]) return;
        lastInteraction = performance.now();
        var ids = pointerIds();

        if (ids.length === 1) {
            var p = pointers[ev.pointerId];
            var dx = ev.clientX - p.x, dy = ev.clientY - p.y;
            if (Math.abs(dx) > 3 || Math.abs(dy) > 3) dragMoved = true;
            pointers[ev.pointerId] = { x: ev.clientX, y: ev.clientY };

            if (currentViewMode === "3d" && !placementActive) {
                desAzimuth -= dx * 0.008;
                desPolar = Math.min(2.6, Math.max(0.35, desPolar - dy * 0.008));
            } else if (!placementActive) {
                // Top/Side/Layer: one-finger drag pans across the fixed view instead of orbiting.
                var panScale = radius * 0.0016;
                var rightVec = new THREE.Vector3().setFromMatrixColumn(camera.matrix, 0);
                var upVec = new THREE.Vector3().setFromMatrixColumn(camera.matrix, 1);
                desTarget.addScaledVector(rightVec, -dx * panScale);
                desTarget.addScaledVector(upVec, dy * panScale);
            }
        } else if (ids.length === 2) {
            var a = pointers[ids[0]], b = pointers[ids[1]];
            var prevDist = Math.hypot(a.x - b.x, a.y - b.y);
            pointers[ev.pointerId] = { x: ev.clientX, y: ev.clientY };
            var a2 = pointers[ids[0]], b2 = pointers[ids[1]];
            var newDist = Math.hypot(a2.x - b2.x, a2.y - b2.y);
            if (prevDist > 1) {
                desRadius = Math.min(maxRadius, Math.max(minRadius, desRadius * (prevDist / newDist)));
                // Pinch always converges the view back onto the truck itself, so zooming
                // never magnifies empty floor off to the side.
                desTarget.lerp(vehicleCenter, 0.15);
                dragMoved = true;
            }
        }
    });

    function boxSelectionKey(mesh) {
        return mesh.userData.groupId != null ? ("g" + mesh.userData.groupId) : ("l" + mesh.userData.lineIndex);
    }

    var raycaster = new THREE.Raycaster();
    function handleTap(ev) {
        if (placementActive) {
            return;
        }

        var rect = canvas.getBoundingClientRect();
        var ndc = new THREE.Vector2(
            ((ev.clientX - rect.left) / rect.width) * 2 - 1,
            -((ev.clientY - rect.top) / rect.height) * 2 + 1
        );
        raycaster.setFromCamera(ndc, camera);

        var boxHits = raycaster.intersectObjects(
            boxMeshes.filter(function (m) { return m.isMesh; }), false);
        if (boxHits.length > 0) {
            var hitMesh = boxHits[0].object;
            var key = boxSelectionKey(hitMesh);
            selectedKey = (selectedKey === key) ? null : key;
            refreshSelectionVisuals();
            if (selectedKey !== null && hitMesh.userData.groupId != null && window.HybridWebView) {
                window.HybridWebView.InvokeDotNet("OnGroupTapped", [hitMesh.userData.groupId]);
            }
            return;
        }

        selectedKey = null;
        focusedGroupId = null;
        refreshSelectionVisuals();
    }

    canvas.addEventListener("pointerup", function (ev) {
        var ids = pointerIds();
        var wasSingle = ids.length === 1;
        delete pointers[ev.pointerId];
        if (wasSingle && !dragMoved) handleTap(ev);
    });
    canvas.addEventListener("pointercancel", function (ev) { delete pointers[ev.pointerId]; });

    function refreshSelectionVisuals() {
        var selectedName = null;
        var hasSelection = selectedKey !== null;
        for (var i = 0; i < boxMeshes.length; i++) {
            var mesh = boxMeshes[i];
            var isSelected = boxSelectionKey(mesh) === selectedKey;
            if (mesh.material) {
                if (mesh.userData.isEdges) {
                    mesh.material.opacity = hasSelection ? (isSelected ? 1 : 0.1) : (mesh.userData.baseOpacity || 0.35);
                    mesh.material.color.setHex(isSelected ? 0x00bfb2 : (mesh.userData.baseLineColor || 0x222222));
                } else {
                    mesh.material.transparent = true;
                    mesh.material.opacity = hasSelection ? (isSelected ? 1 : 0.2) : 0.94;
                    if (mesh.material.emissive) {
                        mesh.material.emissive.setHex(isSelected ? 0x0f9b8e : 0x000000);
                        mesh.material.emissiveIntensity = isSelected ? 0.42 : 0;
                    }
                }
            }
            if (isSelected) selectedName = mesh.userData.name;
        }
        if (!placementActive) {
            hintEl.classList.remove("hint-error", "hint-ok");
            hintEl.textContent = selectedName !== null ? selectedName : "Drag to rotate · pinch to zoom · tap an item";
        }
    }
    refreshSelectionVisuals();

    // ---- SKU rail: interactive chips overlaid on the viewport. Tap = start placing
    // (host opens the placement panel, position chosen by tapping the truck); drag
    // onto the truck = start placing AND set the anchor in one gesture. Rendering is
    // driven by data.skus on every renderLoad, keyed so an unchanged list isn't
    // rebuilt mid-drag. ----
    var lastSkuKey = null;

    function renderSkuRail(skus) {
        skus = skus || [];
        var key = JSON.stringify(skus);
        if (key === lastSkuKey) return;
        lastSkuKey = key;

        skuRailEl.innerHTML = "";
        for (var i = 0; i < skus.length; i++) {
            (function (sku) {
                var chip = document.createElement("div");
                chip.className = "sku-chip" + (sku.enabled ? "" : " disabled");

                var swatch = document.createElement("div");
                swatch.className = "swatch";
                swatch.style.background = sku.color;
                chip.appendChild(swatch);

                var name = document.createElement("div");
                name.className = "name";
                name.textContent = sku.name;
                chip.appendChild(name);

                var meta = document.createElement("div");
                meta.className = "meta";
                meta.textContent = (sku.code ? sku.code + " · " : "") + sku.remaining + " left";
                chip.appendChild(meta);

                if (sku.enabled) {
                    wireChipGestures(chip, sku);
                }
                skuRailEl.appendChild(chip);
            })(skus[i]);
        }
    }

    function wireChipGestures(chip, sku) {
        var startX = 0, startY = 0, chipDragging = false, activePointer = null;
        var dragMesh = null;
        var lastDragZone = null; // { zoneLength, zoneWidth }, the zone under the pointer's last position

        function removeDragMesh() {
            if (dragMesh) {
                scene.remove(dragMesh);
                dragMesh.geometry.dispose();
                dragMesh.material.dispose();
                dragMesh = null;
            }
        }

        // The drag ghost IS a carton: a real 3D box of this SKU's own dimensions gliding
        // across the bed, snapping to whichever of the 6 fixed zones the pointer is over
        // (the zone floor overlay lights up to match) - an exact preview of which zone
        // the drop will land in. Geometric fitment is never checked, so there's no
        // valid/invalid color distinction here any more, just the SKU's own color.
        function updateDragBox(clientX, clientY) {
            var rect = canvas.getBoundingClientRect();
            var over = clientX >= rect.left && clientX <= rect.right && clientY >= rect.top && clientY <= rect.bottom;
            if (!over || !currentVehicle.widthCm) {
                removeDragMesh();
                lastDragZone = null;
                highlightZone(null, null);
                dragGhostEl.textContent = sku.name;
                dragGhostEl.style.display = "block";
                dragGhostEl.style.left = (clientX + 10) + "px";
                dragGhostEl.style.top = (clientY - 14) + "px";
                return;
            }
            dragGhostEl.style.display = "none";

            var ndc = new THREE.Vector2(
                ((clientX - rect.left) / rect.width) * 2 - 1,
                -((clientY - rect.top) / rect.height) * 2 + 1
            );
            planeRaycaster.setFromCamera(ndc, camera);
            raycastPlane.constant = 0;
            var hit = new THREE.Vector3();
            if (!planeRaycaster.ray.intersectPlane(raycastPlane, hit)) return;

            var vx = Math.max(0, Math.min(hit.x + currentVehicle.widthCm / 2, currentVehicle.widthCm));
            var vz = Math.max(0, Math.min(hit.z, currentVehicle.lengthCm));
            var zone = zoneForVehicleXZ(vx, vz);
            lastDragZone = zone;
            highlightZone(zone.zoneLength, zone.zoneWidth);

            // LWH orientation: X-extent = carton length, Z-extent = width, Y-extent = height.
            var ux = sku.l, uz = sku.w, uy = sku.h;
            var zoneLengthCm = currentVehicle.lengthCm / 3;
            var zoneWidthCm = currentVehicle.widthCm / 2;
            var x = zoneWidthNames.indexOf(zone.zoneWidth) * zoneWidthCm;
            var z = zoneLengthNames.indexOf(zone.zoneLength) * zoneLengthCm;

            if (!dragMesh) {
                var geo = new THREE.BoxGeometry(ux, uy, uz);
                var mat = new THREE.MeshLambertMaterial({ transparent: true, opacity: 0.8 });
                dragMesh = new THREE.Mesh(geo, mat);
                var edges = new THREE.EdgesGeometry(geo);
                dragMesh.add(new THREE.LineSegments(edges, new THREE.LineBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.6 })));
                scene.add(dragMesh);
            }
            dragMesh.position.set(x - currentVehicle.widthCm / 2 + ux / 2, uy / 2, z + uz / 2);
            dragMesh.material.color.set(sku.color);
        }

        chip.addEventListener("pointerdown", function (ev) {
            activePointer = ev.pointerId;
            startX = ev.clientX;
            startY = ev.clientY;
            chipDragging = false;
            lastDragZone = null;
            chip.setPointerCapture(ev.pointerId);
            ev.stopPropagation();
        });

        chip.addEventListener("pointermove", function (ev) {
            if (ev.pointerId !== activePointer) return;
            if (!chipDragging && Math.hypot(ev.clientX - startX, ev.clientY - startY) > 8) {
                chipDragging = true;
            }
            if (chipDragging) {
                updateDragBox(ev.clientX, ev.clientY);
            }
            ev.stopPropagation();
        });

        function finish(ev) {
            if (ev.pointerId !== activePointer) return;
            activePointer = null;
            dragGhostEl.style.display = "none";
            removeDragMesh();
            highlightZone(null, null);
            if (!window.HybridWebView) return;

            if (!chipDragging) {
                window.HybridWebView.InvokeDotNet("OnSkuChipTapped", [sku.lineId]);
                return;
            }

            if (lastDragZone) {
                window.HybridWebView.InvokeDotNet("OnSkuChipDropped", [sku.lineId, lastDragZone.zoneLength, lastDragZone.zoneWidth]);
                lastDragZone = null;
            }
        }
        chip.addEventListener("pointerup", finish);
        chip.addEventListener("pointercancel", function (ev) {
            if (ev.pointerId === activePointer) {
                activePointer = null;
                lastDragZone = null;
                dragGhostEl.style.display = "none";
                removeDragMesh();
                highlightZone(null, null);
            }
        });
    }

    // ---- Right rail: saved arrangements, placed groups, and the delivery-unloading
    // search, all as in-viewport overlays. Options/groups re-render only when their
    // payload actually changes; search filters the last groups list locally. ----
    var lastOptionsKey = null;
    var lastGroupsKey = null;
    var groupsData = [];

    function renderOptionsRail(options) {
        options = options || [];
        var key = JSON.stringify(options);
        if (key === lastOptionsKey) return;
        lastOptionsKey = key;

        optionsListEl.innerHTML = "";
        for (var i = 0; i < options.length; i++) {
            (function (opt) {
                var chip = document.createElement("div");
                chip.className = "option-chip" + (opt.isSelected ? " selected" : "");

                var label = document.createElement("div");
                label.className = "label";
                label.textContent = opt.label + " (" + opt.groupCount + ")";
                chip.appendChild(label);

                var metrics = document.createElement("div");
                metrics.className = "metrics" + (opt.warnCount > 0 ? " warn" : "");
                metrics.textContent = "Sp " + Math.round(opt.spacePct) + "% · Wt " + Math.round(opt.weightPct) + "% · " + opt.warnCount + " warn";
                chip.appendChild(metrics);

                var del = document.createElement("div");
                del.className = "del";
                del.textContent = "×";
                del.addEventListener("pointerup", function (ev) {
                    ev.stopPropagation();
                    if (window.HybridWebView) window.HybridWebView.InvokeDotNet("OnOptionDeleted", [opt.id]);
                });
                chip.appendChild(del);

                chip.addEventListener("pointerup", function () {
                    if (window.HybridWebView) window.HybridWebView.InvokeDotNet("OnOptionSelected", [opt.id]);
                });
                optionsListEl.appendChild(chip);
            })(options[i]);
        }
    }

    newOptionBtnEl.addEventListener("pointerup", function () {
        if (window.HybridWebView) window.HybridWebView.InvokeDotNet("OnNewOptionRequested", []);
    });

    document.getElementById("compact-btn").addEventListener("pointerup", function () {
        if (window.HybridWebView) window.HybridWebView.InvokeDotNet("OnCompactRequested", []);
    });

    // Rough "where is it" text from the group's bounds vs the vehicle - same thirds
    // convention as the server's zone labels.
    function positionText(g) {
        var v = currentVehicle;
        if (!v.lengthCm) return "";
        var cz = g.z + g.d / 2, cx = g.x + g.w / 2, cy = g.y + g.h / 2;
        var lengthPart = cz < v.lengthCm / 3 ? "front" : cz < v.lengthCm * 2 / 3 ? "middle" : "back";
        var widthPart = cx < v.widthCm / 3 ? "left" : cx < v.widthCm * 2 / 3 ? "center" : "right";
        var heightPart = cy < v.heightCm / 3 ? "floor" : cy < v.heightCm * 2 / 3 ? "mid" : "top";
        return lengthPart + "-" + widthPart + ", " + heightPart;
    }

    function renderGroupsRail(groups) {
        groups = groups || [];
        var key = JSON.stringify(groups);
        if (key === lastGroupsKey) return;
        lastGroupsKey = key;
        groupsData = groups;

        groupsListEl.innerHTML = "";
        if (groups.length === 0) {
            var empty = document.createElement("div");
            empty.className = "rail-empty";
            empty.textContent = "Nothing placed yet.";
            groupsListEl.appendChild(empty);
        }
        for (var i = 0; i < groups.length; i++) {
            (function (g) {
                var row = document.createElement("div");
                row.className = "group-row";

                var dot = document.createElement("div");
                dot.className = "dot";
                dot.style.background = g.color;
                row.appendChild(dot);

                var text = document.createElement("div");
                var name = document.createElement("div");
                name.className = "g-name";
                name.textContent = g.name + " ×" + g.qty;
                if (g.locked) {
                    // Font Awesome 6 "lock" as inline SVG - the WebView can't load the app's
                    // icon font, but the same library's path data works everywhere.
                    var lock = document.createElementNS("http://www.w3.org/2000/svg", "svg");
                    lock.setAttribute("viewBox", "0 0 448 512");
                    lock.setAttribute("class", "lock-ico");
                    var lockPath = document.createElementNS("http://www.w3.org/2000/svg", "path");
                    lockPath.setAttribute("d", "M144 144v48H304V144c0-44.2-35.8-80-80-80s-80 35.8-80 80zM80 192V144C80 64.5 144.5 0 224 0s144 64.5 144 144v48h16c35.3 0 64 28.7 64 64V448c0 35.3-28.7 64-64 64H64c-35.3 0-64-28.7-64-64V256c0-35.3 28.7-64 64-64H80z");
                    lock.appendChild(lockPath);
                    name.appendChild(lock);
                }
                text.appendChild(name);
                var meta = document.createElement("div");
                meta.className = "g-meta";
                meta.textContent = positionText(g);
                text.appendChild(meta);
                row.appendChild(text);

                row.addEventListener("pointerup", function () {
                    focusOnBox(g.groupId);
                    if (window.HybridWebView) window.HybridWebView.InvokeDotNet("OnGroupTapped", [g.groupId]);
                });
                groupsListEl.appendChild(row);
            })(groups[i]);
        }
        runSearch(); // keep results in sync with the latest groups
    }

    function runSearch() {
        var query = (searchInputEl.value || "").trim().toLowerCase();
        searchResultsEl.innerHTML = "";
        if (query.length === 0) return;

        var matches = groupsData.filter(function (g) {
            return (g.name || "").toLowerCase().indexOf(query) >= 0 ||
                (g.code || "").toLowerCase().indexOf(query) >= 0 ||
                (g.location || "").toLowerCase().indexOf(query) >= 0;
        });

        if (matches.length === 0) {
            var empty = document.createElement("div");
            empty.className = "rail-empty";
            empty.textContent = "No matches.";
            searchResultsEl.appendChild(empty);
            return;
        }

        for (var i = 0; i < matches.length; i++) {
            (function (g) {
                var row = document.createElement("div");
                row.className = "search-result";
                row.dataset.groupId = String(g.groupId);
                if (focusedGroupId === g.groupId) row.classList.add("active");
                var name = document.createElement("div");
                name.className = "s-name";
                name.textContent = g.name + " ×" + g.qty;
                row.appendChild(name);
                var pos = document.createElement("div");
                pos.className = "s-pos";
                pos.textContent = positionText(g) + (g.location ? " · " + g.location : "");
                row.appendChild(pos);
                var show = document.createElement("div");
                show.className = "show-btn";
                show.textContent = "Show";
                show.addEventListener("pointerup", function (ev) {
                    ev.stopPropagation();
                    focusOnBox(g.groupId);
                    if (window.HybridWebView) window.HybridWebView.InvokeDotNet("OnGroupTapped", [g.groupId]);
                });
                row.appendChild(show);
                searchResultsEl.appendChild(row);
            })(matches[i]);
        }
    }
    searchInputEl.addEventListener("input", runSearch);

    function markSearchSelection() {
        var rows = searchResultsEl.querySelectorAll(".search-result");
        for (var i = 0; i < rows.length; i++) {
            rows[i].classList.toggle("active", rows[i].dataset.groupId === String(focusedGroupId));
        }
    }

    function disposeMaterial(material) {
        if (!material) return;
        if (Array.isArray(material)) {
            for (var i = 0; i < material.length; i++) disposeMaterial(material[i]);
            return;
        }
        if (material.map) material.map.dispose();
        material.dispose();
    }

    function disposeObject3D(obj) {
        obj.traverse(function (child) {
            if (child.geometry) child.geometry.dispose();
            disposeMaterial(child.material);
        });
    }

    function drawRoundRect(ctx, x, y, w, h, r) {
        var radius = Math.min(r, w / 2, h / 2);
        ctx.beginPath();
        ctx.moveTo(x + radius, y);
        ctx.lineTo(x + w - radius, y);
        ctx.quadraticCurveTo(x + w, y, x + w, y + radius);
        ctx.lineTo(x + w, y + h - radius);
        ctx.quadraticCurveTo(x + w, y + h, x + w - radius, y + h);
        ctx.lineTo(x + radius, y + h);
        ctx.quadraticCurveTo(x, y + h, x, y + h - radius);
        ctx.lineTo(x, y + radius);
        ctx.quadraticCurveTo(x, y, x + radius, y);
        ctx.closePath();
    }

    function shortText(value, max) {
        var text = (value || "").toString().trim();
        if (text.length <= max) return text;
        return text.substring(0, Math.max(0, max - 1)) + "...";
    }

    function makeLabelSprite(text, options) {
        options = options || {};
        var canvas = document.createElement("canvas");
        canvas.width = options.width || 512;
        canvas.height = options.height || 160;
        var ctx = canvas.getContext("2d");
        var bg = options.background || "rgba(255,255,255,0.92)";
        var border = options.border || "rgba(0,160,145,0.42)";
        var color = options.color || "#1f2933";
        var accent = options.accent || border;

        ctx.clearRect(0, 0, canvas.width, canvas.height);
        ctx.shadowColor = "rgba(24, 39, 48, 0.16)";
        ctx.shadowBlur = 18;
        ctx.shadowOffsetY = 8;
        drawRoundRect(ctx, 16, 18, canvas.width - 32, canvas.height - 36, 24);
        ctx.fillStyle = bg;
        ctx.fill();
        ctx.shadowColor = "transparent";
        ctx.lineWidth = 4;
        ctx.strokeStyle = border;
        ctx.stroke();
        ctx.fillStyle = accent;
        drawRoundRect(ctx, 34, 48, 12, canvas.height - 96, 6);
        ctx.fill();

        ctx.fillStyle = color;
        ctx.font = "700 " + (options.fontSize || 34) + "px Inter, Arial, sans-serif";
        ctx.textBaseline = "middle";
        ctx.fillText(text, 62, canvas.height / 2);

        var texture = new THREE.CanvasTexture(canvas);
        texture.anisotropy = renderer.capabilities.getMaxAnisotropy ? renderer.capabilities.getMaxAnisotropy() : 1;
        var material = new THREE.SpriteMaterial({
            map: texture,
            transparent: true,
            depthTest: false,
            depthWrite: false
        });
        var sprite = new THREE.Sprite(material);
        var sceneWidth = options.sceneWidth || 180;
        sprite.scale.set(sceneWidth, sceneWidth * (canvas.height / canvas.width), 1);
        sprite.renderOrder = options.renderOrder || 30;
        sprite.userData.isLabel = true;
        return sprite;
    }

    function addBox(group, width, height, depth, position, material) {
        var mesh = new THREE.Mesh(new THREE.BoxGeometry(width, height, depth), material);
        mesh.position.copy(position);
        group.add(mesh);
        return mesh;
    }

    function addEdgesForBox(group, width, height, depth, position, color, opacity) {
        var geo = new THREE.BoxGeometry(width, height, depth);
        var edges = new THREE.EdgesGeometry(geo);
        var line = new THREE.LineSegments(edges, new THREE.LineBasicMaterial({
            color: color,
            transparent: opacity < 1,
            opacity: opacity
        }));
        line.position.copy(position);
        group.add(line);
        geo.dispose();
        return line;
    }

    function metersLabel(cm) {
        return (cm / 100).toFixed(1) + "m";
    }

    function buildTruckOutline(vehicle) {
        var group = new THREE.Group();
        var wallMat = new THREE.MeshLambertMaterial({ color: 0xddecea, transparent: true, opacity: 0.24, side: THREE.DoubleSide });
        var glassMat = new THREE.MeshLambertMaterial({ color: 0xe9f6fb, transparent: true, opacity: 0.34, side: THREE.DoubleSide });
        var postMat = new THREE.MeshLambertMaterial({ color: 0x9dafb8, transparent: true, opacity: 0.58 });
        var railMat = new THREE.MeshLambertMaterial({ color: 0x71838e, transparent: true, opacity: 0.72 });
        var floorMat = new THREE.MeshLambertMaterial({ color: 0xc7d6d2, transparent: true, opacity: 0.32 });

        addBox(group, vehicle.widthCm, 6, vehicle.lengthCm, new THREE.Vector3(0, -3, vehicle.lengthCm / 2), floorMat);
        addBox(group, 3, vehicle.heightCm, vehicle.lengthCm, new THREE.Vector3(-vehicle.widthCm / 2, vehicle.heightCm / 2, vehicle.lengthCm / 2), wallMat);
        addBox(group, 3, vehicle.heightCm, vehicle.lengthCm, new THREE.Vector3(vehicle.widthCm / 2, vehicle.heightCm / 2, vehicle.lengthCm / 2), wallMat);
        addBox(group, vehicle.widthCm, vehicle.heightCm, 3, new THREE.Vector3(0, vehicle.heightCm / 2, 0), glassMat);
        addBox(group, vehicle.widthCm, vehicle.heightCm, 3, new THREE.Vector3(0, vehicle.heightCm / 2, vehicle.lengthCm), glassMat);

        addEdgesForBox(group, vehicle.widthCm, vehicle.heightCm, vehicle.lengthCm, new THREE.Vector3(0, vehicle.heightCm / 2, vehicle.lengthCm / 2), 0x6f8290, 0.92);

        var postSize = Math.max(5, Math.min(vehicle.widthCm * 0.035, 10));
        [-1, 1].forEach(function (sx) {
            [0, vehicle.lengthCm].forEach(function (z) {
                addBox(group, postSize, vehicle.heightCm, postSize, new THREE.Vector3(sx * vehicle.widthCm / 2, vehicle.heightCm / 2, z), postMat);
            });
        });

        [-1, 1].forEach(function (sx) {
            addBox(group, 5, 5, vehicle.lengthCm, new THREE.Vector3(sx * vehicle.widthCm / 2, vehicle.heightCm + 2.5, vehicle.lengthCm / 2), railMat);
            addBox(group, 5, 5, vehicle.lengthCm, new THREE.Vector3(sx * vehicle.widthCm / 2, 2.5, vehicle.lengthCm / 2), railMat);
        });
        [0, vehicle.lengthCm].forEach(function (z) {
            addBox(group, vehicle.widthCm, 5, 5, new THREE.Vector3(0, vehicle.heightCm + 2.5, z), railMat);
        });

        var label = makeLabelSprite(
            "Container " + metersLabel(vehicle.lengthCm) + " x " + metersLabel(vehicle.widthCm) + " x " + metersLabel(vehicle.heightCm),
            { sceneWidth: Math.min(260, vehicle.widthCm * 0.85), accent: "#00a393", border: "rgba(0,163,147,0.34)" });
        label.position.set(0, vehicle.heightCm + 44, vehicle.lengthCm * 0.54);
        group.add(label);

        var frontLabel = makeLabelSprite("Front", { sceneWidth: 90, fontSize: 30, accent: "#64748b", border: "rgba(100,116,139,0.28)" });
        frontLabel.position.set(-vehicle.widthCm * 0.32, vehicle.heightCm * 0.52, -18);
        group.add(frontLabel);

        var sideLabel = makeLabelSprite("Side", { sceneWidth: 82, fontSize: 30, accent: "#64748b", border: "rgba(100,116,139,0.28)" });
        sideLabel.position.set(-vehicle.widthCm / 2 - 18, vehicle.heightCm * 0.54, vehicle.lengthCm * 0.5);
        group.add(sideLabel);

        var backLabel = makeLabelSprite("Back", { sceneWidth: 85, fontSize: 30, accent: "#64748b", border: "rgba(100,116,139,0.28)" });
        backLabel.position.set(-vehicle.widthCm * 0.32, vehicle.heightCm * 0.52, vehicle.lengthCm + 18);
        group.add(backLabel);

        return group;
    }

    // The cargo floor is y=0 (all load coordinates key off it), so the running gear hangs
    // BELOW it, like a real truck: wheels tucked under the bed with their tops touching the
    // floor, chassis rails between the axles, cabin ahead of the box, and the ground plane
    // down at road level. Everything stays muted (translucent fills, thin gray lines) so it
    // reads as a truck without competing with the cargo boxes.
    function truckWheelRadius(vehicle) {
        return Math.max(24, Math.min(vehicle.heightCm * 0.14, 46));
    }

    function truckGroundY(vehicle) {
        return -2 * truckWheelRadius(vehicle);
    }

    function buildTruckDetails(vehicle) {
        var group = new THREE.Group();
        var mutedLine = 0x8fa0aa;
        var tireColor = 0x596873;
        var wheelRadius = truckWheelRadius(vehicle);
        var wheelWidth = 20;
        var deckMat = new THREE.MeshLambertMaterial({ color: 0x6e7f89, transparent: true, opacity: 0.48 });
        var guardMat = new THREE.MeshLambertMaterial({ color: 0x9fb0ba, transparent: true, opacity: 0.62 });

        addBox(group, vehicle.widthCm * 1.08, 8, vehicle.lengthCm + 24, new THREE.Vector3(0, -7, vehicle.lengthCm / 2), deckMat);

        // Cabin: sits ahead of the cargo box (Z < 0), base at chassis level.
        var cabinLength = Math.min(140, vehicle.lengthCm * 0.15);
        var cabinHeight = Math.min(vehicle.heightCm * 0.85, 200) + wheelRadius;
        var cabinWidth = vehicle.widthCm * 0.9;
        var cabinGeo = new THREE.BoxGeometry(cabinWidth, cabinHeight, cabinLength);
        var cabinFill = new THREE.Mesh(cabinGeo, new THREE.MeshLambertMaterial({ color: 0xb7c3cc, transparent: true, opacity: 0.34 }));
        cabinFill.position.set(0, cabinHeight / 2 - wheelRadius, -8 - cabinLength / 2);
        group.add(cabinFill);
        var cabinEdges = new THREE.EdgesGeometry(cabinGeo);
        var cabinLines = new THREE.LineSegments(cabinEdges, new THREE.LineBasicMaterial({ color: mutedLine, transparent: true, opacity: 0.85 }));
        cabinLines.position.copy(cabinFill.position);
        group.add(cabinLines);

        var windshieldMat = new THREE.MeshLambertMaterial({ color: 0x9fd3ef, transparent: true, opacity: 0.52 });
        addBox(group, cabinWidth * 0.62, cabinHeight * 0.28, 4, new THREE.Vector3(0, cabinHeight * 0.58, -8 - cabinLength - 1), windshieldMat);
        addBox(group, cabinWidth * 0.38, cabinHeight * 0.2, 4, new THREE.Vector3(-cabinWidth * 0.31, cabinHeight * 0.48, -8 - cabinLength * 0.44), windshieldMat);
        addBox(group, cabinWidth * 0.38, cabinHeight * 0.2, 4, new THREE.Vector3(cabinWidth * 0.31, cabinHeight * 0.48, -8 - cabinLength * 0.44), windshieldMat);
        addBox(group, cabinWidth * 0.78, 10, 8, new THREE.Vector3(0, -wheelRadius * 0.38, -8 - cabinLength - 6), guardMat);

        // Chassis rails: two parallel bars under the bed, running the full truck length.
        var railGeo = new THREE.BoxGeometry(10, 8, vehicle.lengthCm + cabinLength + 8);
        var railMat = new THREE.MeshLambertMaterial({ color: 0x8797a2, transparent: true, opacity: 0.62 });
        [-1, 1].forEach(function (side) {
            var rail = new THREE.Mesh(railGeo, railMat);
            rail.position.set(side * vehicle.widthCm * 0.28, -wheelRadius * 0.35, (vehicle.lengthCm - cabinLength - 8) / 2);
            group.add(rail);
        });

        [-1, 1].forEach(function (side) {
            addBox(group, 8, 10, vehicle.lengthCm * 0.58, new THREE.Vector3(side * (vehicle.widthCm / 2 + 10), -wheelRadius * 0.45, vehicle.lengthCm * 0.48), guardMat);
        });

        // Wheels: tucked under the bed (inboard of the body sides), centers one radius below
        // the floor so their tops touch the bed and they rest on the ground plane.
        var wheelGeo = new THREE.CylinderGeometry(wheelRadius, wheelRadius, wheelWidth, 18);
        var wheelMat = new THREE.MeshLambertMaterial({ color: tireColor, transparent: true, opacity: 0.75 });
        var hubGeo = new THREE.CylinderGeometry(wheelRadius * 0.4, wheelRadius * 0.4, wheelWidth + 2, 12);
        var hubMat = new THREE.MeshLambertMaterial({ color: 0xc0cbd2, transparent: true, opacity: 0.9 });

        function addAxle(z) {
            [-1, 1].forEach(function (side) {
                var x = side * (vehicle.widthCm / 2 - wheelWidth * 0.7);
                var wheel = new THREE.Mesh(wheelGeo, wheelMat);
                wheel.rotation.z = Math.PI / 2;
                wheel.position.set(x, -wheelRadius, z);
                group.add(wheel);
                var hub = new THREE.Mesh(hubGeo, hubMat);
                hub.rotation.z = Math.PI / 2;
                hub.position.set(x, -wheelRadius, z);
                group.add(hub);
            });
        }

        addAxle(-8 - cabinLength * 0.5); // steering axle, under the cabin
        addAxle(vehicle.lengthCm * 0.72); // rear tandem
        addAxle(vehicle.lengthCm * 0.72 + wheelRadius * 2.4);

        var truckLabel = makeLabelSprite(shortText((vehicle.number || "Truck") + (vehicle.typeLabel ? " - " + vehicle.typeLabel : ""), 32), {
            sceneWidth: Math.min(210, vehicle.widthCm * 0.75),
            accent: "#64748b",
            border: "rgba(100,116,139,0.26)"
        });
        truckLabel.position.set(0, Math.max(vehicle.heightCm * 0.46, cabinHeight * 0.62), -8 - cabinLength / 2);
        group.add(truckLabel);

        return group;
    }

    // Ground grid at road level (the truck bed sits a wheel-diameter above it) + simple
    // tick-mark rulers along the bed's own front-bottom (length) and left-bottom (width)
    // edges, marked every metre. Rectangular and hugging the truck's own footprint (a
    // square GridHelper sized to the length left huge empty aprons on both sides that
    // made the truck feel tiny and zoom feel aimless).
    function buildFloorGrid(vehicle) {
        var group = new THREE.Group();
        var margin = 80;
        var gy = truckGroundY(vehicle) + 0.2;
        var gMinX = -vehicle.widthCm / 2 - margin, gMaxX = vehicle.widthCm / 2 + margin;
        var gMinZ = -margin, gMaxZ = vehicle.lengthCm + margin;
        var gridPts = [];
        for (var gx2 = gMinX; gx2 <= gMaxX + 0.1; gx2 += 50) {
            gridPts.push(new THREE.Vector3(gx2, gy, gMinZ), new THREE.Vector3(gx2, gy, gMaxZ));
        }
        for (var gz2 = gMinZ; gz2 <= gMaxZ + 0.1; gz2 += 50) {
            gridPts.push(new THREE.Vector3(gMinX, gy, gz2), new THREE.Vector3(gMaxX, gy, gz2));
        }
        group.add(new THREE.LineSegments(new THREE.BufferGeometry().setFromPoints(gridPts), floorGridMat));

        var tickMat = new THREE.LineBasicMaterial({ color: 0xb2c3bd });
        for (var m = 0; m <= vehicle.lengthCm; m += 100) {
            var z = m;
            var points = [
                new THREE.Vector3(-vehicle.widthCm / 2 - 6, 0.5, z),
                new THREE.Vector3(-vehicle.widthCm / 2 + 6, 0.5, z)
            ];
            group.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints(points), tickMat));
        }
        for (var wm = 0; wm <= vehicle.widthCm; wm += 100) {
            var x = wm - vehicle.widthCm / 2;
            var wpoints = [
                new THREE.Vector3(x, 0.5, -6),
                new THREE.Vector3(x, 0.5, 6)
            ];
            group.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints(wpoints), tickMat));
        }
        return group;
    }

    // The 6 fixed placement zones, drawn as faint floor rectangles so a supervisor can see
    // where a SKU will land before dropping it. zoneMeshes is rebuilt on every vehicle
    // render and consulted by highlightZone() during a SKU-chip drag.
    var zoneMeshes = [];

    function buildZoneOverlay(vehicle) {
        var group = new THREE.Group();
        zoneMeshes = [];
        var zoneLengthCm = vehicle.lengthCm / 3;
        var zoneWidthCm = vehicle.widthCm / 2;
        var gy = truckGroundY(vehicle) + 0.4;

        for (var li = 0; li < zoneLengthNames.length; li++) {
            for (var wi = 0; wi < zoneWidthNames.length; wi++) {
                var centerX = wi * zoneWidthCm + zoneWidthCm / 2 - vehicle.widthCm / 2;
                var centerZ = li * zoneLengthCm + zoneLengthCm / 2;

                var geo = new THREE.PlaneGeometry(Math.max(zoneWidthCm - 4, 1), Math.max(zoneLengthCm - 4, 1));
                geo.rotateX(-Math.PI / 2);
                var mat = new THREE.MeshBasicMaterial({ color: 0x4f7cff, transparent: true, opacity: 0.04, depthWrite: false });
                var mesh = new THREE.Mesh(geo, mat);
                mesh.position.set(centerX, gy, centerZ);
                mesh.userData.zoneLength = zoneLengthNames[li];
                mesh.userData.zoneWidth = zoneWidthNames[wi];
                group.add(mesh);
                zoneMeshes.push(mesh);

                var edgesGeo = new THREE.EdgesGeometry(geo);
                var edgeLines = new THREE.LineSegments(edgesGeo, new THREE.LineBasicMaterial({ color: 0x4f7cff, transparent: true, opacity: 0.25 }));
                group.add(edgeLines);
            }
        }
        return group;
    }

    // Highlights the zone under an active SKU-chip drag; pass (null, null) to clear.
    function highlightZone(zoneLength, zoneWidth) {
        for (var i = 0; i < zoneMeshes.length; i++) {
            var m = zoneMeshes[i];
            var match = zoneLength && m.userData.zoneLength === zoneLength && m.userData.zoneWidth === zoneWidth;
            m.material.opacity = match ? 0.22 : 0.04;
        }
    }

    // Cosmetic-only cap: the server intentionally never limits how many layers a zone can
    // stack (the supervisor applies their own judgement on physical fitment - see
    // LoadPlanValidator, which no longer checks fitment either), so an overloaded zone can
    // report a quantity whose full stack would tower over the vehicle's own roof. The 3D view
    // still shouldn't ever DRAW cargo poking out of the vehicle, so rendering (and the "how
    // many cartons are visible" count used for camera framing) is clamped here - the group's
    // real Quantity, labels and business rules are completely unaffected.
    function visibleQtyForGroup(g, vehicleHeightCm) {
        if (!g.rows || !g.cols || !g.layers || !g.qty) return 0;
        if (!(vehicleHeightCm > 0)) return g.qty;
        var uy = g.h / g.layers;
        if (!(uy > 0)) return g.qty;
        var perLayer = g.rows * g.cols;
        var visualLayers = Math.max(1, Math.min(g.layers, Math.floor((vehicleHeightCm - g.y) / uy)));
        return Math.min(g.qty, perLayer * visualLayers);
    }

    // Per-carton AABBs for a group, capped at its (possibly visually-clamped) quantity and
    // filled the same way the server does (layer -> row -> column). Pure data - used for
    // camera focus and rendering, NOT sent back to the server.
    function cartonsOfGroup(g, vehicleHeightCm) {
        var out = [];
        if (!g.rows || !g.cols || !g.layers || !g.qty) return out;
        var qty = vehicleHeightCm ? visibleQtyForGroup(g, vehicleHeightCm) : g.qty;
        var ux = g.w / g.cols, uy = g.h / g.layers, uz = g.d / g.rows;
        var placed = 0;
        for (var l = 0; l < g.layers && placed < qty; l++) {
            for (var r = 0; r < g.rows && placed < qty; r++) {
                for (var c = 0; c < g.cols && placed < qty; c++) {
                    out.push({ x: g.x + c * ux, y: g.y + l * uy, z: g.z + r * uz, w: ux, h: uy, d: uz, groupId: g.groupId });
                    placed++;
                }
            }
        }
        return out;
    }

    // Renders one GROUP as a handful of slab meshes (full layers as one block, the partial
    // top layer as up to two thinner blocks) plus a single merged line geometry carrying
    // every carton's edges - visually identical to per-carton meshes, but ~40x fewer
    // objects and draw calls, which is what keeps a tablet GPU smooth with 1000+ cartons.
    function buildGroupVisuals(g, vehicleWidth, vehicleHeightCm) {
        var meshes = [];
        if (!g.rows || !g.cols || !g.layers || !g.qty) return meshes;
        var ux = g.w / g.cols, uy = g.h / g.layers, uz = g.d / g.rows;
        var perLayer = g.rows * g.cols;
        var visibleQty = visibleQtyForGroup(g, vehicleHeightCm);
        var fullLayers = Math.floor(visibleQty / perLayer);
        var rem = visibleQty - fullLayers * perLayer;
        var remFullRows = Math.floor(rem / g.cols);
        var remCols = rem - remFullRows * g.cols;

        var mat = new THREE.MeshLambertMaterial({ color: g.color, emissive: 0x000000, transparent: true, opacity: 0.94 });
        var xw = g.x - vehicleWidth / 2; // world-space left edge

        function slab(x, y, z, w, h, d) {
            var mesh = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat);
            mesh.position.set(x + w / 2, y + h / 2, z + d / 2);
            mesh.userData.groupId = g.groupId;
            mesh.userData.name = g.name;
            mesh.userData.yCm = y;
            mesh.userData.hCm = h;
            meshes.push(mesh);
        }

        if (fullLayers > 0) slab(xw, g.y, g.z, g.w, fullLayers * uy, g.d);
        var topY = g.y + fullLayers * uy;
        if (remFullRows > 0) slab(xw, topY, g.z, g.w, uy, remFullRows * uz);
        if (remCols > 0) slab(xw, topY, g.z + remFullRows * uz, remCols * ux, uy, uz);

        // Merged per-carton edge lines (12 edges each) in one geometry.
        var cartons = cartonsOfGroup(g, vehicleHeightCm);
        var verts = new Float32Array(cartons.length * 12 * 2 * 3);
        var o = 0;
        function push(x, y, z) { verts[o++] = x; verts[o++] = y; verts[o++] = z; }
        for (var i = 0; i < cartons.length; i++) {
            var b = cartons[i];
            var x0 = b.x - vehicleWidth / 2, x1 = x0 + b.w, y0 = b.y, y1 = b.y + b.h, z0 = b.z, z1 = b.z + b.d;
            // bottom rectangle, top rectangle, verticals
            push(x0, y0, z0); push(x1, y0, z0); push(x1, y0, z0); push(x1, y0, z1);
            push(x1, y0, z1); push(x0, y0, z1); push(x0, y0, z1); push(x0, y0, z0);
            push(x0, y1, z0); push(x1, y1, z0); push(x1, y1, z0); push(x1, y1, z1);
            push(x1, y1, z1); push(x0, y1, z1); push(x0, y1, z1); push(x0, y1, z0);
            push(x0, y0, z0); push(x0, y1, z0); push(x1, y0, z0); push(x1, y1, z0);
            push(x1, y0, z1); push(x1, y1, z1); push(x0, y0, z1); push(x0, y1, z1);
        }
        var lineGeo = new THREE.BufferGeometry();
        lineGeo.setAttribute("position", new THREE.BufferAttribute(verts, 3));
        var lineMat = new THREE.LineBasicMaterial({
            color: g.locked ? 0x3b3f46 : 0x222222,
            transparent: true,
            opacity: g.locked ? 0.9 : 0.35
        });
        var lines = new THREE.LineSegments(lineGeo, lineMat);
        lines.userData.groupId = g.groupId;
        lines.userData.name = g.name;
        lines.userData.yCm = g.y;
        lines.userData.hCm = g.h;
        lines.userData.isEdges = true;
        lines.userData.baseLineColor = g.locked ? 0x3b3f46 : 0x222222;
        lines.userData.baseOpacity = g.locked ? 0.9 : 0.35;
        meshes.push(lines);

        return meshes;
    }

    // Translucent bounding-box ghost, color driven by validity - green while a
    // placement is valid, red while it would collide or exceed the vehicle.
    // Not tap-selectable (kept out of boxMeshes).
    function buildPreview(preview, vehicleWidth, isValid) {
        var geo = new THREE.BoxGeometry(Math.max(preview.w, 0.1), Math.max(preview.h, 0.1), Math.max(preview.d, 0.1));
        var color = isValid === false ? 0xe0554f : 0x26c281;
        var mat = new THREE.MeshBasicMaterial({ color: color, transparent: true, opacity: 0.35, depthWrite: false });
        var mesh = new THREE.Mesh(geo, mat);
        mesh.position.set(preview.x - vehicleWidth / 2 + preview.w / 2, preview.y + preview.h / 2, preview.z + preview.d / 2);
        var edges = new THREE.EdgesGeometry(geo);
        mesh.add(new THREE.LineSegments(edges, new THREE.LineBasicMaterial({ color: isValid === false ? 0xa8352c : 0x1f8f63 })));
        return mesh;
    }

    // One legend entry per distinct SKU actually placed, plus any SKU the server couldn't place.
    function updateLegend(placed, unplacedNames) {
        legendEl.innerHTML = "";
        var seen = {};
        for (var i = 0; i < placed.length; i++) {
            var box = placed[i];
            if (seen[box.name]) continue;
            seen[box.name] = true;
            var row = document.createElement("div");
            row.className = "legend-item";
            var dot = document.createElement("span");
            dot.className = "legend-dot";
            dot.style.background = box.color;
            var label = document.createElement("span");
            label.textContent = box.name;
            row.appendChild(dot);
            row.appendChild(label);
            legendEl.appendChild(row);
        }
        for (var j = 0; j < unplacedNames.length; j++) {
            if (seen[unplacedNames[j]]) continue;
            seen[unplacedNames[j]] = true;
            var row2 = document.createElement("div");
            row2.className = "legend-item";
            var dot2 = document.createElement("span");
            dot2.className = "legend-dot";
            dot2.style.background = "#999";
            dot2.style.opacity = "0.35";
            var label2 = document.createElement("span");
            label2.textContent = unplacedNames[j] + " (doesn't fit)";
            row2.appendChild(dot2);
            row2.appendChild(label2);
            legendEl.appendChild(row2);
        }
    }

    function updateWarning(unplacedNames) {
        if (unplacedNames.length === 0) {
            warningEl.style.display = "none";
        } else {
            warningEl.textContent = unplacedNames.length + " item(s) couldn't be placed";
            warningEl.style.display = "block";
        }
    }

    function clearGroup() {
        while (sceneGroup.children.length > 0) {
            var child = sceneGroup.children.pop();
            disposeObject3D(child);
        }
        boxMeshes = [];
        ghostMesh = null;
    }

    // data shape: { vehicle: {widthCm, lengthCm, heightCm, number?, typeLabel?},
    //   unplacedNames: [], preview: {x,y,z,w,h,d} | null, previewValid: bool, viewMode: "3d"|"top"|"side"|"sideRight", placementActive: bool,
    //   skus: [{lineId,name,code,color,remaining,enabled}] }
    // Positions always arrive pre-computed by the server - this is a pure renderer
    // (except for the live ghost position while actively dragging, which is
    // purely local for responsiveness and gets reconciled server-side on release).
    window.renderLoad = function (data) {
        var vehicle = data.vehicle;
        currentVehicle = vehicle;
        var vehicleKey = vehicle.widthCm + "x" + vehicle.lengthCm + "x" + vehicle.heightCm;
        if (vehicleKey !== lastVehicleKey) {
            initCameraForVehicle(vehicle);
            lastVehicleKey = vehicleKey;
        }
        rebuildLayerChips(vehicle);
        renderOptionsRail(data.options);
        renderGroupsRail(data.groupsList);
        var infoParts = [];
        if (vehicle.number) infoParts.push(vehicle.number);
        if (vehicle.typeLabel) infoParts.push(vehicle.typeLabel);
        vehicleInfoLineEl.textContent = infoParts.join(" · ");
        vehicleInfoLineEl.style.display = infoParts.length ? "block" : "none";
        edgeLengthLineEl.textContent = "Length " + Math.round(vehicle.lengthCm * 10) + " mm";
        renderSkuRail(data.skus);

        applyViewMode(data.viewMode);
        placementActive = !!data.placementActive;
        placementPlaneY = data.preview ? data.preview.y : 0;
        lastPreviewDims = data.preview ? { w: data.preview.w, h: data.preview.h, d: data.preview.d } : null;

        clearGroup();
        sceneGroup.add(buildTruckOutline(vehicle));
        sceneGroup.add(buildTruckDetails(vehicle));
        sceneGroup.add(buildFloorGrid(vehicle));
        sceneGroup.add(buildZoneOverlay(vehicle));

        var groupsData = data.groupsList || [];
        lastPlacedBoxes = [];
        for (var i = 0; i < groupsData.length; i++) {
            Array.prototype.push.apply(lastPlacedBoxes, cartonsOfGroup(groupsData[i], vehicle.heightCm));
            var visuals = buildGroupVisuals(groupsData[i], vehicle.widthCm, vehicle.heightCm);
            for (var v = 0; v < visuals.length; v++) {
                sceneGroup.add(visuals[v]);
                boxMeshes.push(visuals[v]);
            }
        }
        applyLayerVisibility();

        if (data.preview) {
            var isValid = data.previewValid !== false;
            ghostMesh = buildPreview(data.preview, vehicle.widthCm, isValid);
            sceneGroup.add(ghostMesh);
            ghostValid = isValid;

            hintEl.classList.remove("hint-error", "hint-ok");
            if (isValid) {
                hintEl.classList.add("hint-ok");
                hintEl.textContent = "Valid position - confirm to place it here";
            } else {
                hintEl.classList.add("hint-error");
                hintEl.textContent = data.previewMessage || "Can't place here - overlaps another group or exceeds the vehicle";
            }
        }

        var unplacedNames = data.unplacedNames || [];
        updateLegend(groupsData, unplacedNames);
        updateWarning(unplacedNames);
        refreshSelectionVisuals();

        if (placementActive && !data.preview) {
            hintEl.classList.remove("hint-error", "hint-ok");
            hintEl.textContent = "Tap or drag onto the truck to set a position";
        }
    };

    (function animate() {
        requestAnimationFrame(animate);
        if (currentViewMode === "3d" && !placementActive && pointerIds().length === 0 && performance.now() - lastInteraction > 4000) {
            desAzimuth += 0.0025;
        }
        easeCamera();
        renderer.render(scene, camera);
    })();

    window.addEventListener("HybridWebViewMessageReceived", function (e) {
        try {
            var data = JSON.parse(e.detail.message);
            if (data.command === "focusOnBox") {
                focusOnBox(data.groupId);
            } else {
                window.renderLoad(data);
            }
        } catch (err) {
            showError(err.message || String(err));
        }
    });

    window.onerror = function (message) {
        showError(String(message));
    };
})();
