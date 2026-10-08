<x-app-layout>
    <x-slot name="header">Tambah Ruangan</x-slot>

    <div class="p-6">
        <form action="{{ route('ruangan.store') }}" method="POST">
            @csrf

            <label>Nama Ruangan</label>
            <input type="text" name="nama_ruangan" class="border p-2 w-full">

            <label>Kapasitas</label>
            <input type="number" name="kapasitas" class="border p-2 w-full">

            <button class="bg-blue-500 text-white px-4 py-2 mt-4">Simpan</button>
        </form>
    </div>
</x-app-layout>
