<script lang="ts">
    import { navigate } from "svelte-routing";
    import Main from "../components/templates/Main.svelte";
    import request from "../lib/request";
    import * as rank from "../stores/rank";

    let assetId: string = "";
    let RAPVal: string = "";
    let disabled = false;
    let loading = false;
    let errmsg: string | undefined;
    let successmsg: string | undefined;

    rank.promise.then(() => {
        if (!rank.hasPermission("SetAssetProduct")) {
            errmsg = "You don't have permission to set RAP";
            disabled = true;
        }
    });
    
    async function setRap() {
        errmsg = undefined;
        successmsg = undefined;
        loading = true;

		if (!assetId || RAPVal === null || RAPVal === undefined || RAPVal === "") {
			errmsg = "Both Asset ID and RAP value are required";
			loading = false;
			return;
		}
        
        const RAP = parseFloat(RAPVal);
        if (isNaN(RAP) || RAP < 0 || RAP > 100000000) {
            errmsg = "RAP must be a number between 0 and 100 million";
            loading = false;
            return;
        }
        
        try {
            const response = await request.post(
                `/asset/set-rap`,
                {
                    assetId: parseInt(assetId),
                    rap: RAP
                }
            );
            
            successmsg = `Set RAP for asset ${assetId} to ${RAP.toLocaleString()}`;
            
        } catch (error) {
            errmsg = error.message || "Failed to set RAP, please try again";
        } finally {
            loading = false;
        }
    }
</script>

<svelte:head>
    <title>Set RAP</title>
</svelte:head>

<Main>
    <div class="row">
        <div class="col-12">
            <h1>Set RAP</h1>
            
            {#if errmsg}
                <div class="alert alert-danger">{errmsg}</div>
            {/if}
            
            {#if successmsg}
                <div class="alert alert-success">{successmsg}</div>
            {/if}
        </div>
        
        <div class="col-12 mt-3">
            <form on:submit|preventDefault={setRap}>
                <div class="mb-3">
                    <label for="asset-id" class="form-label">Asset ID *</label>
                    <input 
                        type="number" 
                        class="form-control" 
                        id="asset-id" 
                        bind:value={assetId}
                        required
                        disabled={disabled || loading}
                    />
                </div>
                
                <div class="mb-3">
                    <label for="rap-value" class="form-label">RAP Value *</label>
                    <input 
                        type="number" 
                        class="form-control" 
                        id="rap-value" 
                        bind:value={RAPVal}
                        placeholder="RAP"
                        required
                        disabled={disabled || loading}
                    />
                </div>
                
                <div class="col-12 mt-4">
				<button 
					type="submit" 
					class="btn btn-success"
					disabled={disabled || loading || !assetId}
				>
                        {#if loading}
                            Setting RAP...
                        {:else}
                            Set
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