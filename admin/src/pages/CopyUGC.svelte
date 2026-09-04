<script lang="ts">
    import Main from "../components/templates/Main.svelte";
    import request from "../lib/request";
    import * as rank from "../stores/rank";

    let rbxURL: string = "";
    let OBJ: File | null = null;
    let disabled = false;
    let loading = false;
    let errmsg: string | undefined;
    let result: { assetId?: number, meshId?: number } = {};

    rank.promise.then(() => {
        if (!rank.hasPermission("MigrateAssetFromRoblox")) {
            errmsg = "You don't have permission to copy assets.";
            disabled = true;
        }
    });

    async function migrateAsset() {
        errmsg = undefined;
        result = {};
        loading = true;

        try {
            if (!rbxURL || !OBJ) {
                throw new Error("Roblox URL and OBJ are required");
            }

            const formData = new FormData();
            formData.append("rbxURL", rbxURL);
            formData.append("OBJ", OBJ);

            const response = await request.post(
                "/asset/copy-ugc",
                formData,
                {
                    headers: {
                        'Content-Type': 'multipart/form-data'
                    }
                }
            );

            result = response.data;
        } catch (error: any) {
            errmsg = error.response?.data?.error || 
                    error.message || 
                    "Failed to migrate asset, please try again";
        } finally {
            loading = false;
        }
    }

    function handleupload(event: Event) {
        const input = event.target as HTMLInputElement;
        OBJ = input.files?.[0] ?? null;
    }
</script>

<svelte:head>
    <title>Copy Roblox UGC</title>
</svelte:head>

<Main>
    <div class="row">
        <div class="col-12">
            <h1>Copy Roblox UGC</h1>
            {#if errmsg}
                <div class="alert alert-danger">{errmsg}</div>
            {/if}

            {#if result && result.meshId !== undefined}
                <p>Link: <a href={`/catalog/${result.meshId + 1}/--`}>View on site</a></p>
                <p>Product: <a href={`/admin/product/update?assetId=${result.meshId + 1}`}>Update Product</a></p>
            {/if}
        </div>

        <div class="col-12 mt-3">
            <form on:submit|preventDefault={migrateAsset}>
                <div class="mb-3">
                    <label for="roblox-url" class="form-label">Roblox URL *</label>
                    <input 
                        type="text" 
                        class="form-control" 
                        id="roblox-url" 
                        bind:value={rbxURL}
                        required
                        disabled={disabled || loading}
                    />
                </div>

                <div class="mb-3">
                    <label for="obj-file" class="form-label">OBJ *</label>
                    <input 
                        type="file" 
                        class="form-control"
                        id="obj-file" 
                        accept=".obj"
                        on:change={handleupload}
                        required
                        disabled={disabled || loading}
                    />
                   <div class="form-text">This will also work with any ROBLOX asset that has a newer mesh.</div>
                </div>

                <div class="col-12 mt-4">
                    <button 
                        type="submit" 
                        class="btn btn-success"
                        disabled={disabled || loading || !rbxURL || !OBJ}
                    >
                        {#if loading}
                            Copying...
                        {:else}
                            Copy Asset
                        {/if}
                    </button>
                </div>
            </form>
        </div>
    </div>
</Main>

<style>
    .alert {
        margin-bottom: 1rem;
    }
</style>