classDiagram

note for NetworkInventory "Server-authoritative core.<br/>Transactions are validated and applied on the server.<br/>Observers only project state outward for UI/gameplay consumers."

%% =========================
%% CORE DEFINITIONS
%% =========================

class ItemDefinition {
    <<ScriptableObject>>
    +string Id
    +string Name
    +int MaxStackSize
}

%% =========================
%% RUNTIME DATA
%% =========================

class ItemStack {
    +ItemDefinition Definition
    +int Amount
    +int TryAdd(int amount)
    +int TryRemove(int amount)
    +bool CanMergeWith(ItemStack other)
}

class InventorySlot {
    +int SlotIndex
    +ItemStack Stack
    +bool IsEmpty()
    +void SetStack(ItemStack stack)
    +void Clear()
}

class InventoryChangeRecord {
    +int SlotIndex
    +string ItemId
    +int PreviousAmount
    +int NewAmount
    +string ChangeType
}

%% =========================
%% TRANSACTION SYSTEM
%% =========================

class InventoryTransaction {
    +List~InventoryOperation~ Operations
    +bool Validate(NetworkInventory inv)
    +InventoryTransactionResult Apply(NetworkInventory inv)
}

class InventoryTransactionResult {
    +bool Succeeded
    +List~InventoryChangeRecord~ SlotChanges
    +Dictionary~string,int~ ItemDeltas
    +List~string~ RejectionReasons
    +int SelectedSlotIndex
}

class InventoryOperation {
    <<abstract>>
    +bool CanApply(NetworkInventory inv)
    +void Apply(NetworkInventory inv, InventoryTransactionResult result)
}

class AddItemOperation {
    +ItemDefinition Definition
    +int Amount
}

class RemoveItemOperation {
    +ItemDefinition Definition
    +int Amount
    +int SourceSlotIndex
}

class MoveOperation {
    +int FromSlot
    +int ToSlot
    +int Amount
    +bool AllowMerge
    +bool AllowSwap
}

class SelectSlotOperation {
    +int SlotIndex
}

InventoryTransaction --> InventoryOperation
InventoryOperation <|-- AddItemOperation
InventoryOperation <|-- RemoveItemOperation
InventoryOperation <|-- MoveOperation
InventoryOperation <|-- SelectSlotOperation
InventoryTransaction --> InventoryTransactionResult
InventoryTransactionResult --> InventoryChangeRecord

%% =========================
%% NETWORK INVENTORY (AUTHORITY)
%% =========================

class NetworkInventory {
    <<NetworkBehaviour>>

    -List~InventorySlot~ Slots
    -int Capacity
    -int SelectedSlotIndex

    +bool Server_ExecuteTransaction(InventoryTransaction tx)
    +void Server_SelectSlot(int index)
    +ItemStack GetSelectedItem()
    +ItemStack GetSlot(int index)
    +int CountItem(string itemId)
    +bool CanAcceptItem(string itemId, int amount)

    +void AddObserver(IInventoryObserver observer)
    +void RemoveObserver(IInventoryObserver observer)
    +void NotifyObservers(InventoryTransactionResult result)
}

class IInventoryObserver {
    <<interface>>
    +void OnInventoryChanged(NetworkInventory inventory, InventoryTransactionResult result)
    +void OnSelectedSlotChanged(NetworkInventory inventory, int slotIndex)
}

NetworkInventory --> InventorySlot
NetworkInventory --> InventoryTransaction
NetworkInventory --> IInventoryObserver

%% =========================
%% PROJECTION / VIEW MODELS
%% =========================

class ItemAmountProjection {
    +string ItemId
    +int CachedAmount
    +void Bind(NetworkInventory inv, string itemId)
    +int GetAmount()
}

class InventorySelectionProjection {
    +int CachedSlotIndex
    +void Bind(NetworkInventory inv)
    +int GetSelectedSlotIndex()
}

IInventoryObserver <|.. ItemAmountProjection
IInventoryObserver <|.. InventorySelectionProjection

%% =========================
%% RELATIONSHIPS
%% =========================

ItemStack --> ItemDefinition
InventorySlot --> ItemStack