<x-app-layout>
    <x-slot name="header">Edit Ruangan</x-slot>

    <div class="p-6">
        <form action="{{ route('ruangan.update', $ruangan->id) }}" method="POST">
            @csrf
            @method('PUT')

            <label>Nama Ruangan</label>
            <input type="text" name="nama_ruangan" value="{{ $ruangan->nama_ruangan }}" class="border p-2 w-full">

            <label>Kapasitas</label>
            <input type="number" name="kapasitas" value="{{ $ruangan->kapasitas }}" class="border p-2 w-full">

            <button class="bg-blue-500 text-white px-4 py-2 mt-4">Update</button>
        </form>
    </div>
</x-app-layout>
